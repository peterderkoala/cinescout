using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;

namespace cinescout.core.Matching;

/// <summary>
/// Resolves the FavoriteSeatMatrix applicable to a (Room, Film) pair — a film-specific matrix wins
/// over the room's general (FilmId == null) matrix. Shared by MatchEvaluationService (crawl-time,
/// tracked films only) and SeatAvailabilityQuery (query-time, any performance) so the precedence
/// rule lives in exactly one place.
/// </summary>
public static class SeatMatrixResolver
{
    /// <summary>
    /// Single-performance path: one query loads every enabled matrix for the room, then
    /// <see cref="ResolveApplicable"/> picks the applicable one in memory.
    /// </summary>
    public static async Task<FavoriteSeatMatrix?> ResolveApplicableAsync(
        CineScoutDbContext db, int roomId, int filmId, CancellationToken cancellationToken)
    {
        var candidates = await db.FavoriteSeatMatrices
            .Where(m => m.RoomId == roomId && m.IsEnabled)
            .ToListAsync(cancellationToken);

        return ResolveApplicable(candidates, filmId);
    }

    /// <summary>
    /// Pure precedence rule over an already-loaded set of enabled matrices for one room — lets
    /// batch callers (e.g. SeatAvailabilityQuery's multi-performance overload) load matrices for
    /// many rooms in a single query and resolve each performance in memory, instead of one
    /// DB round-trip per performance.
    /// </summary>
    public static FavoriteSeatMatrix? ResolveApplicable(IReadOnlyList<FavoriteSeatMatrix> enabledMatricesForRoom, int filmId)
    {
        // FirstOrDefault, not Single: a unique index enforces at most one enabled matrix per
        // (RoomId, FilmId) going forward (see CineScoutDbContext), but this stays defensive
        // against any pre-existing/legacy duplicate rather than crashing over it.
        return enabledMatricesForRoom.FirstOrDefault(m => m.FilmId == filmId)
            ?? enabledMatricesForRoom.FirstOrDefault(m => m.FilmId is null);
    }
}
