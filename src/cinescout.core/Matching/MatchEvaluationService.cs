using System.Globalization;
using cinescout.core.Discord;
using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace cinescout.core.Matching;

/// <summary>
/// Evaluates one performance against active preferences after a Kinoheld seat crawl, creating an
/// Active Match the first time a (Performance, WatchedMovie) pair qualifies (never duplicated on
/// re-crawl) and firing RulesMatched/SeatAvailabilityChanged Discord notifications. A no-op for
/// non-watched films or a performance whose Room hasn't been resolved yet.
/// </summary>
public sealed class MatchEvaluationService(CineScoutDbContext db, IDiscordNotifier notifier, ILogger<MatchEvaluationService> logger)
{
    public async Task EvaluateAsync(int performanceId, CancellationToken cancellationToken)
    {
        var performance = await db.Performances.SingleOrDefaultAsync(p => p.Id == performanceId, cancellationToken);
        if (performance is null || performance.RoomId is null)
        {
            return;
        }

        var watchedMovie = await db.WatchedMovies.SingleOrDefaultAsync(w => w.FilmId == performance.FilmId, cancellationToken);
        if (watchedMovie is null)
        {
            return;
        }

        var matrix = await SeatMatrixResolver.ResolveApplicableAsync(db, performance.RoomId.Value, performance.FilmId, cancellationToken);
        var seats = await db.SeatStatuses.Where(s => s.PerformanceId == performanceId).ToListAsync(cancellationToken);
        var block = matrix is not null ? SeatBlockFinder.FindContiguousFreeBlock(seats, matrix) : null;
        var hasSufficientSeats = block is not null;

        var existingMatch = await db.Matches.SingleOrDefaultAsync(
            m => m.PerformanceId == performanceId && m.WatchedMovieId == watchedMovie.Id && m.Status == MatchStatus.Active,
            cancellationToken);

        if (existingMatch is null)
        {
            if (matrix is null || block is null)
            {
                return;
            }

            var windows = await db.FavoriteTimeWindows.ToListAsync(cancellationToken);
            var matchingWindow = TimeWindowMatcher.FindMatchingWindow(performance.StartsAt, windows);
            if (matchingWindow is null)
            {
                return;
            }

            var match = new Match
            {
                PerformanceId = performanceId,
                WatchedMovieId = watchedMovie.Id,
                MatchedAt = DateTimeOffset.UtcNow,
                Status = MatchStatus.Active,
                HasSufficientSeats = true,
            };
            db.Matches.Add(match);
            await db.SaveChangesAsync(cancellationToken);

            await NotifyRulesMatchedAsync(match, performance, matrix, block, matchingWindow, cancellationToken);
        }
        else if (existingMatch.HasSufficientSeats != hasSufficientSeats)
        {
            existingMatch.HasSufficientSeats = hasSufficientSeats;
            await db.SaveChangesAsync(cancellationToken);

            await NotifySeatAvailabilityChangedAsync(existingMatch, performance, hasSufficientSeats, matrix, cancellationToken);
        }
    }

    private async Task<decimal?> FindCheapestMatchingPriceAsync(int performanceId, IReadOnlyList<SeatStatus> block, CancellationToken cancellationToken)
    {
        var providerIds = block
            .Select(s => s.PriceAreaProviderId)
            .Where(id => id is not null)
            .Distinct()
            .ToList();

        if (providerIds.Count == 0)
        {
            return null;
        }

        var prices = await db.PerformancePriceAreas
            .Where(p => p.PerformanceId == performanceId && providerIds.Contains(p.ProviderId))
            .Select(p => p.OrderPrice)
            .ToListAsync(cancellationToken);

        return prices.Count > 0 ? prices.Min() : null;
    }

    private async Task NotifyRulesMatchedAsync(
        Match match, Performance performance, FavoriteSeatMatrix matrix, IReadOnlyList<SeatStatus> block, FavoriteTimeWindow window, CancellationToken cancellationToken)
    {
        var (film, site, room) = await LoadNotificationContextAsync(performance, cancellationToken);
        var price = await FindCheapestMatchingPriceAsync(performance.Id, block, cancellationToken);
        var localStart = CinemaTimeZone.ToLocal(performance.StartsAt);

        var lines = new List<string>
        {
            $"🎬 **{film.Title}** matches your preferences!",
            $"{site.Name} / {room.Name} — {localStart:ddd, dd MMM yyyy HH:mm}",
            $"Falls within your {DescribeWindow(window)} time window, with {block.Count} adjacent free seat(s) (needs {matrix.PartySize}) in \"{matrix.Name}\".",
        };

        if (price is decimal cheapestPrice)
        {
            lines.Add($"From {cheapestPrice.ToString("0.00", CultureInfo.InvariantCulture)} €");
        }

        lines.Add($"Book: <{performance.BookingLink}>");

        if (!string.IsNullOrEmpty(film.PosterUrl))
        {
            lines.Add(film.PosterUrl);
        }

        await SendAndLogAsync(NotificationType.RulesMatched, match, string.Join('\n', lines), cancellationToken);
    }

    private async Task NotifySeatAvailabilityChangedAsync(
        Match match, Performance performance, bool hasSufficientSeats, FavoriteSeatMatrix? matrix, CancellationToken cancellationToken)
    {
        var (film, site, room) = await LoadNotificationContextAsync(performance, cancellationToken);
        var localStart = CinemaTimeZone.ToLocal(performance.StartsAt);
        var partySizeNote = matrix is not null ? $" (needs {matrix.PartySize})" : string.Empty;
        var status = hasSufficientSeats
            ? $"now has enough adjacent free seats again{partySizeNote}"
            : "no longer has enough adjacent free seats";

        var message = string.Join('\n',
        [
            $"🔔 Seat availability changed for **{film.Title}**",
            $"{site.Name} / {room.Name} — {localStart:ddd, dd MMM yyyy HH:mm}",
            $"It {status}.",
            $"Book: <{performance.BookingLink}>",
        ]);

        await SendAndLogAsync(NotificationType.SeatAvailabilityChanged, match, message, cancellationToken);
    }

    private async Task<(Film Film, Site Site, Room Room)> LoadNotificationContextAsync(Performance performance, CancellationToken cancellationToken)
    {
        var film = await db.Films.SingleAsync(f => f.Id == performance.FilmId, cancellationToken);
        var site = await db.Sites.SingleAsync(s => s.Id == performance.SiteId, cancellationToken);
        var room = await db.Rooms.SingleAsync(r => r.Id == performance.RoomId!.Value, cancellationToken);
        return (film, site, room);
    }

    private async Task SendAndLogAsync(NotificationType type, Match match, string message, CancellationToken cancellationToken)
    {
        var result = await NotificationDispatcher.SendAndLogAsync(db, notifier, type, match.Id, message, cancellationToken);

        if (!result.Success)
        {
            logger.LogWarning("Discord notification ({Type}) for Match {MatchId} failed: {Detail}", type, match.Id, result.Detail);
        }
    }

    private static string DescribeWindow(FavoriteTimeWindow window) =>
        $"{window.DaysOfWeek} {window.StartTime:HH:mm}–{window.EndTime:HH:mm}";
}
