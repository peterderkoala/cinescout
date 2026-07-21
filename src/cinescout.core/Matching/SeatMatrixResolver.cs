using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;

namespace cinescout.core.Matching;

/// <summary>
/// Resolves the FavoriteSeatMatrix applicable to a (Room, Film) pair — a film-specific matrix wins
/// over the room's general (FilmId == null) matrix. Shared by MatchEvaluationService (crawl-time,
/// watched films only) and SeatAvailabilityQuery (query-time, any performance) so the precedence
/// rule lives in exactly one place. Static like SeatBlockFinder/TimeWindowMatcher, but unlike those
/// it isn't pure — it queries the DbContext passed in, since matrix precedence is inherently a
/// lookup, not in-memory logic.
/// </summary>
public static class SeatMatrixResolver
{
    public static async Task<FavoriteSeatMatrix?> ResolveApplicableAsync(
        CineScoutDbContext db, int roomId, int filmId, CancellationToken cancellationToken)
    {
        // FirstOrDefault, not SingleOrDefault: a unique index enforces at most one enabled
        // matrix per (RoomId, FilmId) going forward (see CineScoutDbContext), but this stays
        // defensive against any pre-existing/legacy duplicate rather than crashing over it.
        var filmSpecific = await db.FavoriteSeatMatrices.FirstOrDefaultAsync(
            m => m.RoomId == roomId && m.FilmId == filmId && m.IsEnabled, cancellationToken);
        if (filmSpecific is not null)
        {
            return filmSpecific;
        }

        return await db.FavoriteSeatMatrices.FirstOrDefaultAsync(
            m => m.RoomId == roomId && m.FilmId == null && m.IsEnabled, cancellationToken);
    }
}
