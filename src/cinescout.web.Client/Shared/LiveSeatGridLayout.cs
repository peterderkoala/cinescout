using cinescout.contracts;

namespace cinescout.web.Client.Shared;

/// <summary>
/// Arranges one Performance's flat <see cref="LiveSeatDto"/> list into <c>LiveSeatGrid</c>'s
/// rectangular row/cell layout (Technical Design Spec.md §4.2's "live" mode). Rows scan seats
/// ordered by <c>SeatNumber</c> but only treat two seats as visually touching when
/// <c>LeftNeighborSeatId</c> confirms true physical adjacency — the same ground truth
/// <c>cinescout.core.Matching.SeatBlockFinder</c> uses, so a numbering scheme that skips numbers
/// without a real gap doesn't fabricate a hidden cell, and a real gap that happens to sit between
/// consecutive numbers isn't missed (issue #77). Shorter rows are right-padded with hidden cells so
/// every row in the grid has the same width.
/// </summary>
public static class LiveSeatGridLayout
{
    public static IReadOnlyList<LiveSeatGridRow> Build(IReadOnlyList<LiveSeatDto> seats)
    {
        var rows = seats
            .GroupBy(s => s.Row)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new LiveSeatGridRow(g.Key, BuildRowCells(g.OrderBy(s => s.SeatNumber))))
            .ToList();

        if (rows.Count == 0)
        {
            return rows;
        }

        var width = rows.Max(r => r.Cells.Count);
        return [.. rows.Select(r => r with { Cells = Pad(r.Cells, width) })];
    }

    private static IReadOnlyList<LiveSeatGridCell> BuildRowCells(IEnumerable<LiveSeatDto> orderedRowSeats)
    {
        var cells = new List<LiveSeatGridCell>();
        LiveSeatDto? previous = null;

        foreach (var seat in orderedRowSeats)
        {
            if (previous is not null && seat.LeftNeighborSeatId != previous.SourceSeatId)
            {
                cells.Add(LiveSeatGridCell.Hidden);
            }

            cells.Add(new LiveSeatGridCell(seat));
            previous = seat;
        }

        return cells;
    }

    private static IReadOnlyList<LiveSeatGridCell> Pad(IReadOnlyList<LiveSeatGridCell> cells, int width) =>
        cells.Count >= width
            ? cells
            : [.. cells, .. Enumerable.Repeat(LiveSeatGridCell.Hidden, width - cells.Count)];
}

public sealed record LiveSeatGridRow(string Row, IReadOnlyList<LiveSeatGridCell> Cells);

/// <summary>One grid position; <see cref="Seat"/> is null for a hidden/absent cell (§4.2).</summary>
public readonly record struct LiveSeatGridCell(LiveSeatDto? Seat)
{
    public static readonly LiveSeatGridCell Hidden = new(null);
}
