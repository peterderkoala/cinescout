using cinescout.contracts;
using cinescout.web.Client.Shared;

namespace cinescout.web.Client.Tests;

public sealed class LiveSeatGridLayoutTests
{
    private static LiveSeatDto Seat(string id, string row, int seatNumber, SeatOccupancyStatus status = SeatOccupancyStatus.Free, string? left = null, string? right = null) =>
        new()
        {
            SourceSeatId = id,
            Row = row,
            SeatNumber = seatNumber,
            Status = status,
            LeftNeighborSeatId = left,
            RightNeighborSeatId = right,
        };

    [Fact]
    public void Build_FullyAdjacentRow_HasNoHiddenCells()
    {
        var seats = new[]
        {
            Seat("s1", "A", 1, right: "s2"),
            Seat("s2", "A", 2, left: "s1", right: "s3"),
            Seat("s3", "A", 3, left: "s2"),
        };

        var rows = LiveSeatGridLayout.Build(seats);

        var row = Assert.Single(rows);
        Assert.Equal("A", row.Row);
        Assert.Equal(3, row.Cells.Count);
        Assert.All(row.Cells, cell => Assert.NotNull(cell.Seat));
    }

    [Fact]
    public void Build_PhysicalGapDespiteConsecutiveSeatNumbers_InsertsHiddenCell()
    {
        // Seat numbers are consecutive (1, 2) but seat 2's LeftNeighborSeatId doesn't point back
        // to seat 1 — a real gap the numbering alone doesn't reveal (issue #77).
        var seats = new[]
        {
            Seat("s1", "A", 1),
            Seat("s2", "A", 2),
        };

        var rows = LiveSeatGridLayout.Build(seats);

        var row = Assert.Single(rows);
        Assert.Equal(3, row.Cells.Count);
        Assert.NotNull(row.Cells[0].Seat);
        Assert.Null(row.Cells[1].Seat);
        Assert.NotNull(row.Cells[2].Seat);
    }

    [Fact]
    public void Build_NonSequentialNumberingButTrueAdjacency_HasNoHiddenCell()
    {
        // Seat numbers skip (1, 3) but the neighbor chain confirms true physical adjacency — must
        // not fabricate a gap just because the numbers aren't consecutive.
        var seats = new[]
        {
            Seat("s1", "A", 1, right: "s3"),
            Seat("s3", "A", 3, left: "s1"),
        };

        var rows = LiveSeatGridLayout.Build(seats);

        var row = Assert.Single(rows);
        Assert.Equal(2, row.Cells.Count);
        Assert.All(row.Cells, cell => Assert.NotNull(cell.Seat));
    }

    [Fact]
    public void Build_ShorterRowThanOthers_IsPaddedWithHiddenCellsToRectangularWidth()
    {
        var seats = new[]
        {
            Seat("a1", "A", 1, right: "a2"),
            Seat("a2", "A", 2, left: "a1"),
            Seat("b1", "B", 1, right: "b2"),
            Seat("b2", "B", 2, left: "b1", right: "b3"),
            Seat("b3", "B", 3, left: "b2"),
        };

        var rows = LiveSeatGridLayout.Build(seats);

        var rowA = Assert.Single(rows, r => r.Row == "A");
        var rowB = Assert.Single(rows, r => r.Row == "B");
        Assert.Equal(3, rowA.Cells.Count);
        Assert.Equal(3, rowB.Cells.Count);
        Assert.NotNull(rowA.Cells[0].Seat);
        Assert.NotNull(rowA.Cells[1].Seat);
        Assert.Null(rowA.Cells[2].Seat);
    }

    [Fact]
    public void Build_MultipleRows_AreOrderedAlphabeticallyRegardlessOfInputOrder()
    {
        var seats = new[]
        {
            Seat("c1", "C", 1),
            Seat("a1", "A", 1),
            Seat("b1", "B", 1),
        };

        var rows = LiveSeatGridLayout.Build(seats);

        Assert.Equal(["A", "B", "C"], rows.Select(r => r.Row));
    }

    [Fact]
    public void Build_EmptySeatList_ReturnsNoRows()
    {
        var rows = LiveSeatGridLayout.Build([]);

        Assert.Empty(rows);
    }
}
