using cinescout.model;

namespace cinescout.core.Matching;

/// <summary>
/// Pure logic: does the current SeatStatus set for a performance contain a contiguous free block,
/// within a FavoriteSeatMatrix's row/seat-number zone, at least as large as PartySize? No I/O —
/// per #14's testing decision, the matching/rules engine is plain, in-memory logic with no seam.
/// "Contiguous" means true physical adjacency (SeatStatus.LeftNeighborSeatId/RightNeighborSeatId),
/// not merely numerically-consecutive SeatNumbers — Kinoheld's own numbering can have aisle gaps.
/// </summary>
public static class SeatBlockFinder
{
    public static IReadOnlyList<SeatStatus>? FindContiguousFreeBlock(IReadOnlyList<SeatStatus> seats, FavoriteSeatMatrix matrix)
    {
        var zoneSeats = seats.Where(s =>
            string.CompareOrdinal(s.Row, matrix.RowStart) >= 0
            && string.CompareOrdinal(s.Row, matrix.RowEnd) <= 0
            && s.SeatNumber >= matrix.SeatNumberStart
            && s.SeatNumber <= matrix.SeatNumberEnd);

        foreach (var rowGroup in zoneSeats.GroupBy(s => s.Row))
        {
            var block = FindRunInRow(rowGroup.OrderBy(s => s.SeatNumber), matrix.PartySize);
            if (block is not null)
            {
                return block;
            }
        }

        return null;
    }

    private static IReadOnlyList<SeatStatus>? FindRunInRow(IEnumerable<SeatStatus> orderedRowSeats, int partySize)
    {
        var run = new List<SeatStatus>();

        foreach (var seat in orderedRowSeats)
        {
            if (seat.Status != SeatOccupancyStatus.Free)
            {
                run.Clear();
                continue;
            }

            var continuesRun = run.Count > 0 && seat.LeftNeighborSeatId == run[^1].SourceSeatId;
            if (!continuesRun)
            {
                run.Clear();
            }

            run.Add(seat);

            if (run.Count >= partySize)
            {
                return run;
            }
        }

        return null;
    }
}
