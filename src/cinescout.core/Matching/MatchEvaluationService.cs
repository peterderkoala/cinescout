using cinescout.core.Discord;
using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace cinescout.core.Matching;

/// <summary>
/// Evaluates one performance against active preferences after a Kinoheld seat crawl, creating an
/// Active Match the first time a (Performance, TrackedMovie) pair qualifies (never duplicated on
/// re-crawl) and firing RulesMatched/SeatAvailabilityChanged Discord notifications. A no-op for
/// non-tracked films or a performance whose Room hasn't been resolved yet.
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

        var trackedMovie = await db.TrackedMovies.SingleOrDefaultAsync(w => w.FilmId == performance.FilmId, cancellationToken);
        if (trackedMovie is null)
        {
            return;
        }

        var matrix = await SeatMatrixResolver.ResolveApplicableAsync(db, performance.RoomId.Value, performance.FilmId, cancellationToken);
        var seats = await db.SeatStatuses.Where(s => s.PerformanceId == performanceId).ToListAsync(cancellationToken);
        var block = matrix is not null ? SeatBlockFinder.FindContiguousFreeBlock(seats, matrix) : null;
        var hasSufficientSeats = block is not null;

        var existingMatch = await db.Matches.SingleOrDefaultAsync(
            m => m.PerformanceId == performanceId && m.TrackedMovieId == trackedMovie.Id,
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
                TrackedMovieId = trackedMovie.Id,
                MatchedAt = DateTimeOffset.UtcNow,
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
        var (film, cinema, room) = await LoadNotificationContextAsync(performance, cancellationToken);
        var price = await FindCheapestMatchingPriceAsync(performance.Id, block, cancellationToken);
        var message = NotificationMessages.RulesMatched(film, cinema, room, performance, window, matrix, block.Count, price, DateTimeOffset.UtcNow);

        await SendAndLogAsync(NotificationType.RulesMatched, match, film.Id, message, cancellationToken);
    }

    private async Task NotifySeatAvailabilityChangedAsync(
        Match match, Performance performance, bool hasSufficientSeats, FavoriteSeatMatrix? matrix, CancellationToken cancellationToken)
    {
        var (film, cinema, room) = await LoadNotificationContextAsync(performance, cancellationToken);
        var message = NotificationMessages.SeatAvailabilityChanged(film, cinema, room, performance, hasSufficientSeats, matrix, DateTimeOffset.UtcNow);

        await SendAndLogAsync(NotificationType.SeatAvailabilityChanged, match, film.Id, message, cancellationToken);
    }

    private async Task<(Film Film, Cinema Cinema, Room Room)> LoadNotificationContextAsync(Performance performance, CancellationToken cancellationToken)
    {
        var film = await db.Films.SingleAsync(f => f.Id == performance.FilmId, cancellationToken);
        var cinema = await db.Cinemas.SingleAsync(s => s.Id == performance.CinemaId, cancellationToken);
        var room = await db.Rooms.SingleAsync(r => r.Id == performance.RoomId!.Value, cancellationToken);
        return (film, cinema, room);
    }

    private async Task SendAndLogAsync(NotificationType type, Match match, int filmId, NotificationMessage message, CancellationToken cancellationToken)
    {
        var results = await NotificationDispatcher.SendAndLogAsync(db, notifier, type, match.Id, filmId, message, cancellationToken);

        foreach (var failed in results.Where(r => !r.Success))
        {
            logger.LogWarning("Discord notification ({Type}) for Match {MatchId} failed: {Detail}", type, match.Id, failed.Detail);
        }
    }
}
