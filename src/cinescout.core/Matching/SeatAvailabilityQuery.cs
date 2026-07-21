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
}
