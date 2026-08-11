using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace cinescout.core.Kinoheld;

/// <summary>
/// Seeds/upserts <see cref="Room"/> rows for a <see cref="Cinema"/> from its Kinoheld widget
/// config's auditorium list. Deliberately split out from the ToS-sensitive seat-level crawler
/// (#8) so the preferences UI can get a room picker without needing that crawler built yet.
///
/// Room seeding is eager, not lazy: run once per Cinema (e.g. at app startup), independent of any
/// regular seat crawl. It derives the Kinoheld widget URL to fetch from any already-crawled
/// <see cref="Performance.BookingLink"/> for the cinema (any bookingLink 301-redirects to the same
/// cinema-wide widget page), rather than from a dedicated Cinema field — if nothing has been
/// crawled yet for a cinema, seeding is a safe no-op that will pick up once a Hall-of-Fame crawl
/// has run.
///
/// Also the Re-seed Rooms trigger (Cinemas screen, ADR 0005): the circuit breaker and a per-cinema
/// cooldown are checked internally, symmetric with how <see cref="KinoheldSeatCrawlService"/>
/// already gates its own calls — callers don't enforce either themselves.
/// </summary>
public sealed class KinoheldRoomSeedingService(
    CineScoutDbContext db,
    IKinoheldClient client,
    KinoheldCircuitBreaker circuitBreaker,
    KinoheldRoomSeedCooldownTracker cooldownTracker,
    ILogger<KinoheldRoomSeedingService> logger)
{
    public async Task<RoomSeedOutcome> SeedRoomsForCinemaAsync(Cinema cinema, CancellationToken cancellationToken)
    {
        if (circuitBreaker.IsTripped)
        {
            return RoomSeedOutcome.CircuitOpen;
        }

        var bookingLink = await db.Performances
            .Where(p => p.CinemaId == cinema.Id)
            .OrderByDescending(p => p.StartsAt)
            .Select(p => p.BookingLink)
            .FirstOrDefaultAsync(cancellationToken);

        if (bookingLink is null)
        {
            return RoomSeedOutcome.Unavailable; // nothing crawled yet for this cinema to derive a widget URL from — safe no-op, will pick up once Hall-of-Fame crawl has run
        }

        var now = DateTimeOffset.UtcNow;
        if (!cooldownTracker.TryBeginSeed(cinema.Id, now))
        {
            return RoomSeedOutcome.CooldownActive;
        }

        var result = await client.GetWidgetConfigAsync(bookingLink, cancellationToken);

        switch (result)
        {
            case KinoheldWidgetConfigResult.Success success:
                await UpsertAsync(cinema, success.Config, cancellationToken);
                return RoomSeedOutcome.Seeded;

            case KinoheldWidgetConfigResult.Blocked blocked:
                circuitBreaker.Trip($"Kinoheld answered HTTP {blocked.StatusCode} to a widget-config fetch for cinema {cinema.Id} — treating as a block; all Kinoheld polling is stopped until app restart.");
                logger.LogError(
                    "Kinoheld blocked a widget-config request (HTTP {StatusCode}) for cinema {CinemaId}; circuit breaker tripped, all polling stopped.",
                    blocked.StatusCode,
                    cinema.Id);
                return RoomSeedOutcome.CircuitOpen;

            case KinoheldWidgetConfigResult.Anomalous anomalous:
                circuitBreaker.Trip($"Anomalous Kinoheld widget-config response for cinema {cinema.Id}: {anomalous.Detail} All Kinoheld polling is stopped until app restart.");
                logger.LogError(
                    "Anomalous Kinoheld widget-config response for cinema {CinemaId}: {Detail}; circuit breaker tripped, all polling stopped.",
                    cinema.Id,
                    anomalous.Detail);
                return RoomSeedOutcome.CircuitOpen;

            default:
                throw new InvalidOperationException($"Unhandled KinoheldWidgetConfigResult variant {result.GetType().Name}.");
        }
    }

    private async Task UpsertAsync(Cinema cinema, KinoheldWidgetConfig config, CancellationToken cancellationToken)
    {
        // Capture Kinoheld's numeric cinema id alongside the auditoriums — the seat crawl (#22)
        // needs it as "cid" and never guesses it.
        if (cinema.KinoheldCinemaId != config.CinemaId)
        {
            cinema.KinoheldCinemaId = config.CinemaId;
        }

        foreach (var auditorium in config.Auditoriums)
        {
            var room = await db.Rooms.SingleOrDefaultAsync(
                r => r.CinemaId == cinema.Id && r.ExternalAuditoriumId == auditorium.Id, cancellationToken);

            if (room is null)
            {
                db.Rooms.Add(new Room { CinemaId = cinema.Id, ExternalAuditoriumId = auditorium.Id, Name = auditorium.Name });
            }
            else if (room.Name != auditorium.Name)
            {
                room.Name = auditorium.Name;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
