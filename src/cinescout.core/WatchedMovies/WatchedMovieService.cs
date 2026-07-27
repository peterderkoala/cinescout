using cinescout.core.Discord;
using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;

namespace cinescout.core.WatchedMovies;

/// <summary>
/// Marks/unmarks a Film as watched. Whether it's still "active" (has upcoming performances) is
/// a query-time filter elsewhere, not tracked here — this only owns the WatchedMovie row's
/// existence and the WatchStarted/WatchStopped notification lifecycle.
/// </summary>
public sealed class WatchedMovieService(CineScoutDbContext db, IDiscordNotifier notifier)
{
    public async Task WatchAsync(int filmId, CancellationToken cancellationToken)
    {
        var alreadyWatched = await db.WatchedMovies.AnyAsync(w => w.FilmId == filmId, cancellationToken);
        if (alreadyWatched)
        {
            return;
        }

        db.WatchedMovies.Add(new WatchedMovie { FilmId = filmId, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(cancellationToken);

        await NotifyAsync(NotificationType.WatchStarted, filmId, cancellationToken);
    }

    public async Task UnwatchAsync(int filmId, CancellationToken cancellationToken)
    {
        var watchedMovie = await db.WatchedMovies.SingleOrDefaultAsync(w => w.FilmId == filmId, cancellationToken);
        if (watchedMovie is null)
        {
            return;
        }

        db.WatchedMovies.Remove(watchedMovie);
        await db.SaveChangesAsync(cancellationToken);

        await NotifyAsync(NotificationType.WatchStopped, filmId, cancellationToken);
    }

    private async Task NotifyAsync(NotificationType type, int filmId, CancellationToken cancellationToken)
    {
        var film = await db.Films.SingleAsync(f => f.Id == filmId, cancellationToken);
        var message = type switch
        {
            NotificationType.WatchStarted =>
                $"Now watching **{film.Title}** — you'll be notified when a screening matches your preferences.",
            NotificationType.WatchStopped => $"Stopped watching **{film.Title}**.",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Only watch-lifecycle notification types are sent from here."),
        };

        await NotificationDispatcher.SendAndLogAsync(db, notifier, type, matchId: null, message, cancellationToken);
    }
}
