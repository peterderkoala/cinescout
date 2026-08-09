using cinescout.core.Discord;
using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;

namespace cinescout.core.TrackedMovies;

/// <summary>
/// Marks/unmarks a Film as tracked. Whether it's still "active" (has upcoming performances) is
/// a query-time filter elsewhere, not tracked here — this only owns the TrackedMovie row's
/// existence and the TrackStarted/TrackStopped notification lifecycle.
/// </summary>
public sealed class TrackedMovieService(CineScoutDbContext db, IDiscordNotifier notifier)
{
    public async Task TrackAsync(int filmId, CancellationToken cancellationToken)
    {
        var alreadyTracked = await db.TrackedMovies.AnyAsync(w => w.FilmId == filmId, cancellationToken);
        if (alreadyTracked)
        {
            return;
        }

        db.TrackedMovies.Add(new TrackedMovie { FilmId = filmId, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(cancellationToken);

        await NotifyAsync(NotificationType.TrackStarted, filmId, cancellationToken);
    }

    public async Task UntrackAsync(int filmId, CancellationToken cancellationToken)
    {
        var trackedMovie = await db.TrackedMovies.SingleOrDefaultAsync(w => w.FilmId == filmId, cancellationToken);
        if (trackedMovie is null)
        {
            return;
        }

        db.TrackedMovies.Remove(trackedMovie);
        await db.SaveChangesAsync(cancellationToken);

        await NotifyAsync(NotificationType.TrackStopped, filmId, cancellationToken);
    }

    private async Task NotifyAsync(NotificationType type, int filmId, CancellationToken cancellationToken)
    {
        var film = await db.Films.SingleAsync(f => f.Id == filmId, cancellationToken);
        var message = type switch
        {
            NotificationType.TrackStarted =>
                $"Now tracking **{film.Title}** — you'll be notified when a screening matches your preferences.",
            NotificationType.TrackStopped => $"Stopped tracking **{film.Title}**.",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Only track-lifecycle notification types are sent from here."),
        };

        await NotificationDispatcher.SendAndLogAsync(db, notifier, type, matchId: null, message, cancellationToken);
    }
}
