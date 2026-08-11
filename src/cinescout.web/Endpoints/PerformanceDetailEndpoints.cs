using System.Globalization;
using cinescout.contracts;
using cinescout.core.Kinoheld;
using cinescout.core.Matching;
using cinescout.model;
using cinescout.persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace cinescout.web.Endpoints;

/// <summary>
/// The Performance Detail screen's API surface (Technical Design Spec.md §5.7, #98): own read
/// model, independent of HomeEndpoints, per #83's "each Endpoints/ file is self-contained" rule —
/// status badge/match reasons are recomputed here the same way, single-performance instead of
/// Home's batch. POST force-refresh calls through unchanged to
/// <see cref="KinoheldSeatCrawlService.FetchForPerformanceAsync"/> (breaker/cooldown enforcement
/// already lives inside that service per ADR 0004) and translates its <see cref="SeatFetchOutcome"/>
/// to <c>ProblemDetails</c> + <c>kinoheldStatus</c>, the same convention
/// <c>CinemasEndpoints.ReseedRoomsAsync</c> established for <c>RoomSeedOutcome</c>. The four-outcome
/// alert table (§5.7) is therefore transient force-refresh-response state, not part of the GET DTO.
///
/// GET also calls <c>FetchForPerformanceAsync(forceRefresh: false, ...)</c> before reading — ported
/// forward from the legacy static-SSR stub this replaces (<c>cinescout.web/Components/Pages/
/// PerformanceDetail.razor</c>, deleted by this ticket) and matching that method's own doc comment
/// ("on-demand path for the performance detail page"): it's the freshness-window-gated, cooldown-
/// exempt path that's the *only* way a non-tracked film's performance ever gets live seat data at
/// all, since the recurring crawl (<c>CrawlTrackedAsync</c>) only covers tracked films. The outcome
/// is deliberately discarded here — a silent view-triggered refresh failing (breaker tripped, not
/// bookable, etc.) must not block the page from rendering with whatever data already exists; only
/// the user's own explicit Force Refresh click surfaces an alert.
/// </summary>
public static class PerformanceDetailEndpointsExtensions
{
    public static RouteGroupBuilder MapPerformanceDetailEndpoints(this RouteGroupBuilder apiGroup)
    {
        var group = apiGroup.MapGroup("/performances");

        group.MapGet("/{id:int}", GetDetailAsync);
        group.MapPost("/{id:int}/force-refresh", ForceRefreshAsync);

        return apiGroup;
    }

    private static async Task<Results<Ok<PerformanceDetailDto>, NotFound>> GetDetailAsync(
        int id, KinoheldSeatCrawlService crawlService, CineScoutDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Performances.AnyAsync(p => p.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        await crawlService.FetchForPerformanceAsync(id, forceRefresh: false, cancellationToken);

        var dto = await BuildDetailDtoAsync(db, id, cancellationToken)
            ?? throw new InvalidOperationException($"Performance {id} existed a moment ago but could not be re-read.");

        return TypedResults.Ok(dto);
    }

    private static async Task<Results<Ok<PerformanceDetailDto>, NotFound, ProblemHttpResult>> ForceRefreshAsync(
        int id, KinoheldSeatCrawlService crawlService, CineScoutDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Performances.AnyAsync(p => p.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var outcome = await crawlService.FetchForPerformanceAsync(id, forceRefresh: true, cancellationToken);

        switch (outcome)
        {
            case SeatFetchOutcome.Fetched:
                var dto = await BuildDetailDtoAsync(db, id, cancellationToken)
                    ?? throw new InvalidOperationException($"Performance {id} existed a moment ago but could not be re-read after force refresh.");
                return TypedResults.Ok(dto);

            case SeatFetchOutcome.CircuitOpen:
                return ProblemWithKinoheldStatus(
                    "breaker-open",
                    "Kinoheld connection unavailable.",
                    "The Kinoheld connection's circuit breaker is open — all Kinoheld polling is stopped until the app restarts.",
                    StatusCodes.Status503ServiceUnavailable);

            case SeatFetchOutcome.NotBookable:
                return ProblemWithKinoheldStatus(
                    "not-bookable",
                    "Not currently bookable.",
                    "This performance isn't currently bookable on Kinoheld.",
                    StatusCodes.Status409Conflict);

            case SeatFetchOutcome.NotFound:
                return ProblemWithKinoheldStatus(
                    "gone",
                    "No longer available.",
                    "This performance is no longer available on Kinoheld.",
                    StatusCodes.Status410Gone);

            case SeatFetchOutcome.CooldownActive:
                return ProblemWithKinoheldStatus(
                    "cooldown",
                    "Refreshed too recently.",
                    "Refreshed too recently — force refresh is available again in 30 seconds.",
                    StatusCodes.Status429TooManyRequests);

            case SeatFetchOutcome.Unavailable:
                return TypedResults.Problem(
                    title: "Live seat data unavailable.",
                    detail: "This performance's cinema hasn't been through Kinoheld room seeding yet, so there's no way to fetch live seats.",
                    statusCode: StatusCodes.Status409Conflict);

            default:
                throw new InvalidOperationException($"Unhandled {nameof(SeatFetchOutcome)} value {outcome} from a force refresh (SeatFetchOutcome.Fresh can't occur since forceRefresh is always true here).");
        }
    }

    private static async Task<PerformanceDetailDto?> BuildDetailDtoAsync(CineScoutDbContext db, int id, CancellationToken cancellationToken)
    {
        var performance = await db.Performances.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (performance is null)
        {
            return null;
        }

        var film = await db.Films.SingleAsync(f => f.Id == performance.FilmId, cancellationToken);
        var cinema = await db.Cinemas.SingleAsync(c => c.Id == performance.CinemaId, cancellationToken);
        var roomName = performance.RoomId is int roomId
            ? await db.Rooms.Where(r => r.Id == roomId).Select(r => r.Name).SingleAsync(cancellationToken)
            : "";

        var seats = await db.SeatStatuses.Where(s => s.PerformanceId == id).ToListAsync(cancellationToken);
        var prices = await db.PerformancePriceAreas.Where(p => p.PerformanceId == id).Select(p => p.OrderPrice).ToListAsync(cancellationToken);
        var tracking = await db.TrackedMovies.AnyAsync(t => t.FilmId == performance.FilmId, cancellationToken);
        var activeMatch = await db.Matches.SingleOrDefaultAsync(m => m.PerformanceId == id && m.Status == MatchStatus.Active, cancellationToken);

        var hasSufficientSeats = false;
        var reasons = new List<string>();
        if (performance.RoomId is int matrixRoomId)
        {
            var matrix = await SeatMatrixResolver.ResolveApplicableAsync(db, matrixRoomId, performance.FilmId, cancellationToken);
            var block = matrix is not null ? SeatBlockFinder.FindContiguousFreeBlock(seats, matrix) : null;
            hasSufficientSeats = block is not null;

            if (activeMatch is not null && block is not null)
            {
                reasons.Add($"{block.Count} seat{(block.Count == 1 ? "" : "s")} free");
            }
        }

        if (activeMatch is not null)
        {
            var windows = await db.FavoriteTimeWindows.ToListAsync(cancellationToken);
            if (TimeWindowMatcher.FindMatchingWindow(performance.StartsAt, windows) is not null)
            {
                reasons.Add(DescribeDayPart(CinemaTimeZone.ToLocal(performance.StartsAt)));
            }
        }

        var card = new FilmPerformanceCardModel
        {
            Title = film.Title,
            Cinema = cinema.Name,
            Room = roomName,
            DateTime = PerformanceDateTimeFormatting.FormatListDateTime(CinemaTimeZone.ToLocal(performance.StartsAt).DateTime),
            Price = prices.Count > 0 ? FormatPrice(prices.Min()) : null,
            Status = DeriveStatus(performance, hasSufficientSeats),
            Tracking = tracking,
            IsMatch = activeMatch is not null,
            MatchReasons = reasons,
            PosterUrl = film.PosterUrl,
            BookingLink = performance.BookingLink,
        };

        return new PerformanceDetailDto
        {
            Card = card,
            Seats = seats.Select(s => new LiveSeatDto
            {
                SourceSeatId = s.SourceSeatId,
                Row = s.Row,
                SeatNumber = s.SeatNumber,
                Status = (cinescout.contracts.SeatOccupancyStatus)(int)s.Status,
                LeftNeighborSeatId = s.LeftNeighborSeatId,
                RightNeighborSeatId = s.RightNeighborSeatId,
            }).ToList(),
        };
    }

    /// <summary>§6.1's precedence: Cancelled beats sold-out beats seats-available beats no badge.</summary>
    private static PerformanceCardStatus DeriveStatus(Performance performance, bool hasSufficientSeats) => performance switch
    {
        { Status: PerformanceStatus.Cancelled } => PerformanceCardStatus.Cancelled,
        { IsSoldOut: true } => PerformanceCardStatus.SoldOut,
        _ when hasSufficientSeats => PerformanceCardStatus.Available,
        _ => PerformanceCardStatus.None,
    };

    private static string FormatPrice(decimal orderPrice) => $"€{orderPrice.ToString("0.00", CultureInfo.InvariantCulture)}";

    /// <summary>§6.2's "{Day} {part-of-day}" match reason, e.g. "Fri evening".</summary>
    private static string DescribeDayPart(DateTimeOffset localStart)
    {
        var day = localStart.ToString("ddd", CultureInfo.InvariantCulture);
        var part = localStart.Hour switch
        {
            < 12 => "morning",
            < 17 => "afternoon",
            < 21 => "evening",
            _ => "night",
        };

        return $"{day} {part}";
    }

    private static ProblemHttpResult ProblemWithKinoheldStatus(string kinoheldStatus, string title, string detail, int statusCode) =>
        TypedResults.Problem(
            title: title,
            detail: detail,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["kinoheldStatus"] = kinoheldStatus });
}
