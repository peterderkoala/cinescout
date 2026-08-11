using System.Globalization;
using cinescout.contracts;
using cinescout.core.Matching;
using cinescout.model;
using cinescout.persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace cinescout.web.Endpoints;

/// <summary>
/// The Home screen's API surface (Technical Design Spec.md §5.6, #97): a single GET assembles
/// featured Active <c>Match</c> cards, the Tracked Movies panel (max 5), and Recent Activity (last
/// 5 <c>NotificationLog</c> rows) — #83's "one screen, one round trip" convention, self-contained
/// per every other screen's <c>Endpoints/</c> file (no writes: Home triggers no Kinoheld-calling
/// action itself, per the ticket's §8 Error state note).
///
/// Match reasons (§6.2) are recomputed at read time via <see cref="SeatMatrixResolver"/> /
/// <see cref="SeatBlockFinder"/> / <see cref="TimeWindowMatcher"/> — the same pure functions
/// <c>MatchEvaluationService</c> used to create the Match, so the two can't disagree on what counts
/// as "enough seats". <c>Match.HasSufficientSeats</c> only persists a bool, not the seat count or
/// which window matched, so the "{n} seats free" / "{Day} {part-of-day}" reason text needs the
/// recompute regardless.
/// </summary>
public static class HomeEndpointsExtensions
{
    public static RouteGroupBuilder MapHomeEndpoints(this RouteGroupBuilder apiGroup)
    {
        apiGroup.MapGet("/home", GetAsync);

        return apiGroup;
    }

    private static async Task<Ok<HomePageDto>> GetAsync(
        CineScoutDbContext db, SeatAvailabilityQuery seatAvailability, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        return TypedResults.Ok(new HomePageDto
        {
            ActiveMatches = await BuildActiveMatchesAsync(db, cancellationToken),
            HasAnyTrackedMovie = await db.TrackedMovies.AnyAsync(cancellationToken),
            TrackedMovies = await BuildTrackedMoviesAsync(db, seatAvailability, now, cancellationToken),
            RecentActivity = await BuildRecentActivityAsync(db, cancellationToken),
        });
    }

    private static async Task<IReadOnlyList<FilmPerformanceCardModel>> BuildActiveMatchesAsync(
        CineScoutDbContext db, CancellationToken cancellationToken)
    {
        var matches = await db.Matches.Where(m => m.Status == MatchStatus.Active).ToListAsync(cancellationToken);
        if (matches.Count == 0)
        {
            return [];
        }

        var performanceIds = matches.Select(m => m.PerformanceId).Distinct().ToList();
        var performances = await db.Performances.Where(p => performanceIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);

        var filmIds = performances.Values.Select(p => p.FilmId).Distinct().ToList();
        var films = await db.Films.Where(f => filmIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);

        var cinemaIds = performances.Values.Select(p => p.CinemaId).Distinct().ToList();
        var cinemas = await db.Cinemas.Where(c => cinemaIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);

        var roomIds = performances.Values.Where(p => p.RoomId is not null).Select(p => p.RoomId!.Value).Distinct().ToList();
        var rooms = await db.Rooms.Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);

        var matricesByRoom = (await db.FavoriteSeatMatrices
                .Where(m => roomIds.Contains(m.RoomId) && m.IsEnabled)
                .ToListAsync(cancellationToken))
            .ToLookup(m => m.RoomId);
        var seatsByPerformance = (await db.SeatStatuses
                .Where(s => performanceIds.Contains(s.PerformanceId))
                .ToListAsync(cancellationToken))
            .ToLookup(s => s.PerformanceId);
        var priceAreasByPerformance = (await db.PerformancePriceAreas
                .Where(p => performanceIds.Contains(p.PerformanceId))
                .ToListAsync(cancellationToken))
            .ToLookup(p => p.PerformanceId);
        var windows = await db.FavoriteTimeWindows.ToListAsync(cancellationToken);

        var cards = new List<(DateTimeOffset StartsAt, FilmPerformanceCardModel Card)>();

        foreach (var match in matches)
        {
            if (!performances.TryGetValue(match.PerformanceId, out var performance) || performance.RoomId is not int roomId)
            {
                // A Match can only be created once Performance.RoomId is resolved (MatchEvaluationService),
                // so this is defensive against a Performance later going stale, not an expected path.
                continue;
            }

            var film = films[performance.FilmId];
            var cinema = cinemas[performance.CinemaId];
            var room = rooms[roomId];
            var localStart = CinemaTimeZone.ToLocal(performance.StartsAt);

            var reasons = BuildMatchReasons(match, performance, localStart, matricesByRoom[roomId].ToList(), seatsByPerformance[performance.Id].ToList(), windows);
            var prices = priceAreasByPerformance[performance.Id].Select(p => p.OrderPrice).ToList();

            var card = new FilmPerformanceCardModel
            {
                Title = film.Title,
                Cinema = cinema.Name,
                Room = room.Name,
                DateTime = PerformanceDateTimeFormatting.FormatListDateTime(localStart.DateTime),
                Price = prices.Count > 0 ? FormatPrice(prices.Min()) : null,
                Status = DeriveStatus(performance, match.HasSufficientSeats),
                Tracking = true,
                IsMatch = true,
                MatchReasons = reasons,
                PosterUrl = film.PosterUrl,
                BookingLink = performance.BookingLink,
            };

            cards.Add((performance.StartsAt, card));
        }

        return cards.OrderBy(c => c.StartsAt).Select(c => c.Card).ToList();
    }

    private static IReadOnlyList<string> BuildMatchReasons(
        Match match,
        Performance performance,
        DateTimeOffset localStart,
        IReadOnlyList<FavoriteSeatMatrix> enabledMatricesForRoom,
        IReadOnlyList<SeatStatus> seatsForPerformance,
        IReadOnlyList<FavoriteTimeWindow> windows)
    {
        var reasons = new List<string>();

        if (match.HasSufficientSeats)
        {
            var matrix = SeatMatrixResolver.ResolveApplicable(enabledMatricesForRoom, performance.FilmId);
            var block = matrix is not null ? SeatBlockFinder.FindContiguousFreeBlock(seatsForPerformance, matrix) : null;
            if (block is not null)
            {
                reasons.Add($"{block.Count} seat{(block.Count == 1 ? "" : "s")} free");
            }
        }

        if (TimeWindowMatcher.FindMatchingWindow(performance.StartsAt, windows) is not null)
        {
            reasons.Add(DescribeDayPart(localStart));
        }

        return reasons;
    }

    private static async Task<IReadOnlyList<TrackedMovieHomeRowDto>> BuildTrackedMoviesAsync(
        CineScoutDbContext db, SeatAvailabilityQuery seatAvailability, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var tracked = await db.TrackedMovies
            .Join(db.Films, tm => tm.FilmId, f => f.Id, (tm, f) => new { f.Id, f.Title })
            .OrderBy(x => x.Title)
            .Take(5)
            .ToListAsync(cancellationToken);

        if (tracked.Count == 0)
        {
            return [];
        }

        var filmIds = tracked.Select(t => t.Id).ToList();
        var nextByFilm = (await db.Performances
                .Where(p => filmIds.Contains(p.FilmId) && p.StartsAt >= now && p.Status != PerformanceStatus.Cancelled)
                .ToListAsync(cancellationToken))
            .GroupBy(p => p.FilmId)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.StartsAt).First());

        var nextPerformances = nextByFilm.Values.ToList();
        var hasOpenSeats = await seatAvailability.HasOpenFavoriteMatrixSeatsAsync(nextPerformances, cancellationToken);

        var roomIds = nextPerformances.Where(p => p.RoomId is not null).Select(p => p.RoomId!.Value).Distinct().ToList();
        var rooms = await db.Rooms.Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);

        var rows = new List<TrackedMovieHomeRowDto>();
        foreach (var film in tracked)
        {
            if (!nextByFilm.TryGetValue(film.Id, out var performance))
            {
                rows.Add(new TrackedMovieHomeRowDto { FilmTitle = film.Title, NextPerformance = null });
                continue;
            }

            var roomName = performance.RoomId is int roomId && rooms.TryGetValue(roomId, out var room) ? room.Name : "";
            var localStart = CinemaTimeZone.ToLocal(performance.StartsAt);

            rows.Add(new TrackedMovieHomeRowDto
            {
                FilmTitle = film.Title,
                NextPerformance = new FilmPerformanceCardModel
                {
                    Title = film.Title,
                    Cinema = null,
                    Room = roomName,
                    DateTime = PerformanceDateTimeFormatting.FormatListDateTime(localStart.DateTime),
                    Price = null,
                    Status = DeriveStatus(performance, hasOpenSeats.GetValueOrDefault(performance.Id)),
                    Tracking = true,
                    IsMatch = false,
                    MatchReasons = [],
                    PosterUrl = null,
                    BookingLink = performance.BookingLink,
                },
            });
        }

        return rows;
    }

    private static async Task<IReadOnlyList<RecentActivityDto>> BuildRecentActivityAsync(CineScoutDbContext db, CancellationToken cancellationToken)
    {
        var logs = await db.NotificationLogs
            .Where(n => n.FilmId != null)
            .OrderByDescending(n => n.SentAt)
            .Take(5)
            .ToListAsync(cancellationToken);

        if (logs.Count == 0)
        {
            return [];
        }

        var filmIds = logs.Select(l => l.FilmId!.Value).Distinct().ToList();
        var films = await db.Films.Where(f => filmIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);

        return logs
            .Where(l => films.ContainsKey(l.FilmId!.Value))
            .Select(l => new RecentActivityDto { FilmTitle = films[l.FilmId!.Value].Title, SentAt = l.SentAt })
            .ToList();
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
}
