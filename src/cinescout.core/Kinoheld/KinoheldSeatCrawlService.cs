using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace cinescout.core.Kinoheld;

/// <summary>
/// Fetches and stores Kinoheld seat availability, both on the recurring schedule (watched films'
/// upcoming performances only) and on demand from the performance detail page. Every outgoing
/// call is gated by the <see cref="KinoheldCircuitBreaker"/> — the moment Kinoheld pushes back
/// (403/429/anomalous), all polling stops until an app restart. HTTP 400 ("not currently
/// bookable") and 404 ("show not found") are expected per-performance outcomes: logged, skipped,
/// retried next cycle, never a trip.
/// </summary>
public sealed class KinoheldSeatCrawlService(
    CineScoutDbContext db,
    IKinoheldClient client,
    KinoheldCircuitBreaker circuitBreaker,
    KinoheldFetchCooldownTracker cooldownTracker,
    IConfiguration configuration,
    ILogger<KinoheldSeatCrawlService> logger)
{
    /// <summary>
    /// Recurring-crawl path: fetches seats for every upcoming, non-cancelled performance of a
    /// currently-watched film. Stops iterating immediately if the circuit breaker trips mid-run.
    /// </summary>
    public async Task CrawlWatchedAsync(CancellationToken cancellationToken)
    {
        if (circuitBreaker.IsTripped)
        {
            logger.LogWarning(
                "Kinoheld circuit breaker is tripped (reason: {Reason}, at {TrippedAt}); skipping seat crawl entirely.",
                circuitBreaker.TripReason,
                circuitBreaker.TrippedAt);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var watchedFilmIds = db.WatchedMovies.Select(w => w.FilmId);
        var performances = await db.Performances
            .Where(p => watchedFilmIds.Contains(p.FilmId)
                && p.StartsAt >= now
                && p.Status == PerformanceStatus.Normal)
            .OrderBy(p => p.StartsAt)
            .ToListAsync(cancellationToken);

        foreach (var performance in performances)
        {
            var outcome = await FetchAndStoreAsync(performance, cancellationToken);
            if (outcome == SeatFetchOutcome.CircuitOpen)
            {
                logger.LogWarning(
                    "Kinoheld circuit breaker tripped mid-crawl; stopping immediately with remaining performances unfetched.");
                return;
            }
        }
    }

    /// <summary>
    /// On-demand path for the performance detail page. Serves cache when the latest snapshot is
    /// within the freshness window (<c>Kinoheld:SeatFreshnessMinutes</c>, default 30) unless
    /// <paramref name="forceRefresh"/> is set; every actual outgoing call (forced or not) is
    /// recorded against the 30-second per-performance cooldown.
    /// </summary>
    public async Task<SeatFetchOutcome> FetchForPerformanceAsync(int performanceId, bool forceRefresh, CancellationToken cancellationToken)
    {
        if (circuitBreaker.IsTripped)
        {
            return SeatFetchOutcome.CircuitOpen;
        }

        var performance = await db.Performances.SingleOrDefaultAsync(p => p.Id == performanceId, cancellationToken);
        if (performance is null)
        {
            logger.LogWarning("On-demand seat fetch requested for unknown performance {PerformanceId}.", performanceId);
            return SeatFetchOutcome.Unavailable;
        }

        var now = DateTimeOffset.UtcNow;

        if (!forceRefresh)
        {
            var freshnessMinutes = configuration.GetValue("Kinoheld:SeatFreshnessMinutes", 30);
            var latestCrawledAt = await db.SeatingSnapshots
                .Where(s => s.PerformanceId == performanceId)
                .MaxAsync(s => (DateTimeOffset?)s.CrawledAt, cancellationToken);

            if (latestCrawledAt is not null && now - latestCrawledAt.Value < TimeSpan.FromMinutes(freshnessMinutes))
            {
                return SeatFetchOutcome.Fresh;
            }
        }

        if (!cooldownTracker.TryBeginFetch(performance.Id, now))
        {
            return SeatFetchOutcome.CooldownActive;
        }

        return await FetchAndStoreAsync(performance, cancellationToken);
    }

    /// <summary>
    /// Shared fetch-and-store core. <paramref name="performance"/> must be tracked by this
    /// service's <see cref="CineScoutDbContext"/> (both public paths load it that way).
    /// </summary>
    private async Task<SeatFetchOutcome> FetchAndStoreAsync(Performance performance, CancellationToken cancellationToken)
    {
        var cinemaId = await db.Sites
            .Where(s => s.Id == performance.SiteId)
            .Select(s => s.KinoheldCinemaId)
            .SingleOrDefaultAsync(cancellationToken);

        if (cinemaId is null)
        {
            // Never guess the cid — it's captured by room seeding and simply may not be there yet.
            logger.LogWarning(
                "Skipping seat fetch for performance {PerformanceId}: its Site {SiteId} has no KinoheldCinemaId yet (room seeding hasn't captured it).",
                performance.Id,
                performance.SiteId);
            return SeatFetchOutcome.Unavailable;
        }

        var result = await client.GetSeatsAsync(cinemaId, performance.SourcePerformanceId, cancellationToken);

        switch (result)
        {
            case KinoheldSeatsResult.Success success:
                await StoreAsync(performance, success, cancellationToken);
                return SeatFetchOutcome.Fetched;

            case KinoheldSeatsResult.NotBookable:
                logger.LogInformation(
                    "Performance {PerformanceId} (show {ShowId}) is not currently bookable on Kinoheld (HTTP 400); skipping, will retry next cycle.",
                    performance.Id,
                    performance.SourcePerformanceId);
                return SeatFetchOutcome.NotBookable;

            case KinoheldSeatsResult.NotFound:
                logger.LogInformation(
                    "Performance {PerformanceId} (show {ShowId}) no longer exists on Kinoheld (HTTP 404); skipping.",
                    performance.Id,
                    performance.SourcePerformanceId);
                return SeatFetchOutcome.NotFound;

            case KinoheldSeatsResult.Blocked blocked:
                circuitBreaker.Trip($"Kinoheld answered HTTP {blocked.StatusCode} to a getSeats request for show {performance.SourcePerformanceId} — treating as a block; all Kinoheld polling is stopped until app restart.");
                logger.LogError(
                    "Kinoheld blocked a getSeats request (HTTP {StatusCode}) for performance {PerformanceId}; circuit breaker tripped, all polling stopped.",
                    blocked.StatusCode,
                    performance.Id);
                return SeatFetchOutcome.CircuitOpen;

            case KinoheldSeatsResult.Anomalous anomalous:
                circuitBreaker.Trip($"Anomalous Kinoheld getSeats response for show {performance.SourcePerformanceId}: {anomalous.Detail} All Kinoheld polling is stopped until app restart.");
                logger.LogError(
                    "Anomalous Kinoheld getSeats response for performance {PerformanceId}: {Detail}; circuit breaker tripped, all polling stopped.",
                    performance.Id,
                    anomalous.Detail);
                return SeatFetchOutcome.CircuitOpen;

            default:
                throw new InvalidOperationException($"Unhandled KinoheldSeatsResult variant {result.GetType().Name}.");
        }
    }

    private async Task StoreAsync(Performance performance, KinoheldSeatsResult.Success success, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var snapshot = new SeatingSnapshot
        {
            PerformanceId = performance.Id,
            CrawledAt = now,
            RawPayload = success.RawPayload,
        };
        db.SeatingSnapshots.Add(snapshot);
        // SeatingSnapshotSeat is FK-only (no navigation property), so we need the real generated
        // Id before building the detail rows below — save immediately, matching the
        // HallOfFameCrawlService snapshot pattern.
        await db.SaveChangesAsync(cancellationToken);

        foreach (var seat in success.Seats)
        {
            db.SeatingSnapshotSeats.Add(new SeatingSnapshotSeat
            {
                SnapshotId = snapshot.Id,
                SourceSeatId = seat.SourceSeatId,
                Row = seat.Row,
                SeatNumber = seat.SeatNumber,
                Status = MapStatus(seat.RawStatus),
                LeftNeighborSeatId = seat.LeftNeighborSeatId,
                RightNeighborSeatId = seat.RightNeighborSeatId,
            });
        }

        // SeatStatus is the current-state mirror of the latest crawl: update in place, add new,
        // and remove rows for seats absent from this response.
        var existingStatuses = await db.SeatStatuses
            .Where(s => s.PerformanceId == performance.Id)
            .ToListAsync(cancellationToken);
        var existingBySeatId = existingStatuses.ToDictionary(s => s.SourceSeatId);
        var seenSeatIds = new HashSet<string>();

        foreach (var seat in success.Seats)
        {
            seenSeatIds.Add(seat.SourceSeatId);

            if (existingBySeatId.TryGetValue(seat.SourceSeatId, out var existing))
            {
                existing.Row = seat.Row;
                existing.SeatNumber = seat.SeatNumber;
                existing.Status = MapStatus(seat.RawStatus);
                existing.LeftNeighborSeatId = seat.LeftNeighborSeatId;
                existing.RightNeighborSeatId = seat.RightNeighborSeatId;
                existing.UpdatedAt = now;
            }
            else
            {
                db.SeatStatuses.Add(new SeatStatus
                {
                    PerformanceId = performance.Id,
                    SourceSeatId = seat.SourceSeatId,
                    Row = seat.Row,
                    SeatNumber = seat.SeatNumber,
                    Status = MapStatus(seat.RawStatus),
                    LeftNeighborSeatId = seat.LeftNeighborSeatId,
                    RightNeighborSeatId = seat.RightNeighborSeatId,
                    UpdatedAt = now,
                });
            }
        }

        db.SeatStatuses.RemoveRange(existingStatuses.Where(s => !seenSeatIds.Contains(s.SourceSeatId)));

        // First successful response resolves the performance's room via the seeded Room table
        // (the Hall-of-Fame schedule has no room concept; secId is the auditorium's external id).
        if (performance.RoomId is null && success.Seats.Count > 0)
        {
            var sectorId = success.Seats[0].SectorId;
            var room = await db.Rooms.SingleOrDefaultAsync(
                r => r.SiteId == performance.SiteId && r.ExternalAuditoriumId == sectorId,
                cancellationToken);

            if (room is null)
            {
                logger.LogWarning(
                    "No Room with ExternalAuditoriumId {SectorId} found for Site {SiteId}; leaving performance {PerformanceId}'s RoomId null.",
                    sectorId,
                    performance.SiteId,
                    performance.Id);
            }
            else
            {
                performance.RoomId = room.Id;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static SeatOccupancyStatus MapStatus(string rawStatus) => rawStatus switch
    {
        "sf" => SeatOccupancyStatus.Free,
        "ss" => SeatOccupancyStatus.Sold,
        _ => SeatOccupancyStatus.Other,
    };
}
