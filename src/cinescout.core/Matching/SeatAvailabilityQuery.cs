using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;

namespace cinescout.core.Matching;

/// <summary>
/// Query-time capability (#24): does this performance currently have an open contiguous seat block
/// satisfying its applicable FavoriteSeatMatrix? Works for any performance being browsed, not just
/// ones tied to a watched movie — unlike MatchEvaluationService, which only persists a Match (and
/// notifies) for watched films. Reuses SeatMatrixResolver/SeatBlockFinder, the same logic the
/// crawl-time evaluation uses, so the two never disagree on what counts as "enough seats".
/// </summary>
public sealed class SeatAvailabilityQuery(CineScoutDbContext db)
{
    public async Task<bool> HasOpenFavoriteMatrixSeatsAsync(int performanceId, CancellationToken cancellationToken)
    {
        var performance = await db.Performances.SingleOrDefaultAsync(p => p.Id == performanceId, cancellationToken);
        if (performance?.RoomId is not int roomId)
        {
            return false;
        }

        var matrix = await SeatMatrixResolver.ResolveApplicableAsync(db, roomId, performance.FilmId, cancellationToken);
        if (matrix is null)
        {
            return false;
        }

        var seats = await db.SeatStatuses.Where(s => s.PerformanceId == performanceId).ToListAsync(cancellationToken);
        return SeatBlockFinder.FindContiguousFreeBlock(seats, matrix) is not null;
    }

    /// <summary>
    /// Batch form for browse pages listing many performances (e.g. Schedule.razor) — two bulk
    /// queries (matrices across every distinct RoomId, seats across every performance) instead of
    /// one round-trip per performance, then resolves each performance's matrix/block in memory via
    /// SeatMatrixResolver.ResolveApplicable/SeatBlockFinder. Callers already hold the Performance
    /// rows (e.g. from their own listing query), so this takes them directly rather than re-fetching.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, bool>> HasOpenFavoriteMatrixSeatsAsync(
        IReadOnlyList<Performance> performances, CancellationToken cancellationToken)
    {
        var roomIds = performances.Where(p => p.RoomId is not null).Select(p => p.RoomId!.Value).Distinct().ToList();
        var matricesByRoom = (await db.FavoriteSeatMatrices
                .Where(m => roomIds.Contains(m.RoomId) && m.IsEnabled)
                .ToListAsync(cancellationToken))
            .ToLookup(m => m.RoomId);

        var performanceIds = performances.Select(p => p.Id).ToList();
        var seatsByPerformance = (await db.SeatStatuses
                .Where(s => performanceIds.Contains(s.PerformanceId))
                .ToListAsync(cancellationToken))
            .ToLookup(s => s.PerformanceId);

        var result = new Dictionary<int, bool>();
        foreach (var performance in performances)
        {
            if (performance.RoomId is not int roomId)
            {
                result[performance.Id] = false;
                continue;
            }

            var matrix = SeatMatrixResolver.ResolveApplicable(matricesByRoom[roomId].ToList(), performance.FilmId);
            result[performance.Id] = matrix is not null
                && SeatBlockFinder.FindContiguousFreeBlock(seatsByPerformance[performance.Id].ToList(), matrix) is not null;
        }

        return result;
    }
}
