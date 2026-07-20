using cinescout.core.Matching;
using cinescout.model;

namespace cinescout.core.Tests;

public class SeatBlockFinderTests
{
    private static SeatStatus Seat(string id, string row, int number, SeatOccupancyStatus status, string? left, string? right) => new()
    {
        SourceSeatId = id,
        Row = row,
        SeatNumber = number,
        Status = status,
        LeftNeighborSeatId = left,
        RightNeighborSeatId = right,
    };

    private static FavoriteSeatMatrix Matrix(string rowStart, string rowEnd, int seatStart, int seatEnd, int partySize) => new()
    {
        RoomId = 1,
        Name = "Test matrix",
        RowStart = rowStart,
        RowEnd = rowEnd,
        SeatNumberStart = seatStart,
        SeatNumberEnd = seatEnd,
        PartySize = partySize,
        IsEnabled = true,
    };

    [Fact]
    public void Finds_a_contiguous_free_block_meeting_party_size()
    {
        var seats = new[]
        {
            Seat("d1", "D", 1, SeatOccupancyStatus.Free, null, "d2"),
            Seat("d2", "D", 2, SeatOccupancyStatus.Free, "d1", "d3"),
            Seat("d3", "D", 3, SeatOccupancyStatus.Free, "d2", "d4"),
            Seat("d4", "D", 4, SeatOccupancyStatus.Free, "d3", null),
        };

        var block = SeatBlockFinder.FindContiguousFreeBlock(seats, Matrix("D", "D", 1, 4, partySize: 3));

        Assert.NotNull(block);
        Assert.True(block.Count >= 3);
    }

    [Fact]
    public void Returns_null_when_no_run_meets_party_size()
    {
        var seats = new[]
        {
            Seat("d1", "D", 1, SeatOccupancyStatus.Free, null, "d2"),
            Seat("d2", "D", 2, SeatOccupancyStatus.Free, "d1", "d3"),
            Seat("d3", "D", 3, SeatOccupancyStatus.Free, "d2", "d4"),
            Seat("d4", "D", 4, SeatOccupancyStatus.Free, "d3", null),
        };

        var block = SeatBlockFinder.FindContiguousFreeBlock(seats, Matrix("D", "D", 1, 4, partySize: 5));

        Assert.Null(block);
    }

    [Fact]
    public void Excludes_seats_outside_the_declared_zone_even_if_free_and_adjacent()
    {
        var seats = new[]
        {
            Seat("d1", "D", 1, SeatOccupancyStatus.Free, null, "d2"),
            Seat("d2", "D", 2, SeatOccupancyStatus.Free, "d1", "d3"),
            Seat("d3", "D", 3, SeatOccupancyStatus.Free, "d2", "d4"),
            // d4 is free and physically adjacent to d3, but outside the matrix's declared zone
            // (SeatNumberEnd = 3) — it must not extend the in-zone run.
            Seat("d4", "D", 4, SeatOccupancyStatus.Free, "d3", null),
        };

        var fourPartyBlock = SeatBlockFinder.FindContiguousFreeBlock(seats, Matrix("D", "D", 1, 3, partySize: 4));
        Assert.Null(fourPartyBlock);

        var threePartyBlock = SeatBlockFinder.FindContiguousFreeBlock(seats, Matrix("D", "D", 1, 3, partySize: 3));
        Assert.NotNull(threePartyBlock);
    }

    [Fact]
    public void A_sold_seat_breaks_the_run()
    {
        var seats = new[]
        {
            Seat("d1", "D", 1, SeatOccupancyStatus.Free, null, "d2"),
            Seat("d2", "D", 2, SeatOccupancyStatus.Free, "d1", "d3"),
            Seat("d3", "D", 3, SeatOccupancyStatus.Sold, "d2", "d4"),
            Seat("d4", "D", 4, SeatOccupancyStatus.Free, "d3", "d5"),
            Seat("d5", "D", 5, SeatOccupancyStatus.Free, "d4", null),
        };

        // 4 free seats total in the zone, but no single contiguous run of 3 — d3 splits them 2+2.
        var block = SeatBlockFinder.FindContiguousFreeBlock(seats, Matrix("D", "D", 1, 5, partySize: 3));

        Assert.Null(block);
    }

    [Fact]
    public void Non_adjacent_seats_do_not_combine_despite_consecutive_seat_numbers()
    {
        // d2 and d3 are numerically consecutive but not linked as neighbors (e.g. an aisle) —
        // adjacency, not numbering, is what defines "contiguous".
        var seats = new[]
        {
            Seat("d1", "D", 1, SeatOccupancyStatus.Free, null, "d2"),
            Seat("d2", "D", 2, SeatOccupancyStatus.Free, "d1", null),
            Seat("d3", "D", 3, SeatOccupancyStatus.Free, null, "d4"),
            Seat("d4", "D", 4, SeatOccupancyStatus.Free, "d3", null),
        };

        var fourPartyBlock = SeatBlockFinder.FindContiguousFreeBlock(seats, Matrix("D", "D", 1, 4, partySize: 4));
        Assert.Null(fourPartyBlock);

        var twoPartyBlock = SeatBlockFinder.FindContiguousFreeBlock(seats, Matrix("D", "D", 1, 4, partySize: 2));
        Assert.NotNull(twoPartyBlock);
    }

    [Fact]
    public void Finds_a_block_in_a_later_row_when_an_earlier_row_in_the_zone_has_none()
    {
        var seats = new[]
        {
            Seat("d1", "D", 1, SeatOccupancyStatus.Sold, null, "d2"),
            Seat("d2", "D", 2, SeatOccupancyStatus.Free, "d1", null),
            Seat("e1", "E", 1, SeatOccupancyStatus.Free, null, "e2"),
            Seat("e2", "E", 2, SeatOccupancyStatus.Free, "e1", "e3"),
            Seat("e3", "E", 3, SeatOccupancyStatus.Free, "e2", null),
        };

        var block = SeatBlockFinder.FindContiguousFreeBlock(seats, Matrix("D", "E", 1, 3, partySize: 3));

        Assert.NotNull(block);
        Assert.All(block, s => Assert.Equal("E", s.Row));
    }

    [Fact]
    public void Exact_party_size_boundary_succeeds_one_more_than_available_fails()
    {
        var seats = new[]
        {
            Seat("d1", "D", 1, SeatOccupancyStatus.Free, null, "d2"),
            Seat("d2", "D", 2, SeatOccupancyStatus.Free, "d1", "d3"),
            Seat("d3", "D", 3, SeatOccupancyStatus.Free, "d2", null),
        };

        Assert.NotNull(SeatBlockFinder.FindContiguousFreeBlock(seats, Matrix("D", "D", 1, 3, partySize: 3)));
        Assert.Null(SeatBlockFinder.FindContiguousFreeBlock(seats, Matrix("D", "D", 1, 3, partySize: 4)));
    }
}
