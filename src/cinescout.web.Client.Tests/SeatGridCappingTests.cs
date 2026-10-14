using cinescout.web.Client.Shared;

namespace cinescout.web.Client.Tests;

public sealed class SeatGridCappingTests
{
    [Fact]
    public void Compute_ExactlyAtCap_HasNoGhostRowOrColumn()
    {
        var result = SeatGridCapping.Compute(rowCount: 5, seatCount: 25);

        Assert.Equal(5, result.RowsShown);
        Assert.Equal(25, result.SeatsShown);
        Assert.False(result.HasGhostRow);
        Assert.False(result.HasGhostColumn);
        Assert.Equal("", result.OverflowCaption);
    }

    [Fact]
    public void Compute_OneRowOverCap_ShowsSingularGhostRowCaption()
    {
        var result = SeatGridCapping.Compute(rowCount: 6, seatCount: 25);

        Assert.Equal(5, result.RowsShown);
        Assert.True(result.HasGhostRow);
        Assert.False(result.HasGhostColumn);
        Assert.Equal("+1 row", result.OverflowCaption);
    }

    [Fact]
    public void Compute_OneSeatOverCap_ShowsSingularGhostColumnCaption()
    {
        var result = SeatGridCapping.Compute(rowCount: 5, seatCount: 26);

        Assert.Equal(25, result.SeatsShown);
        Assert.False(result.HasGhostRow);
        Assert.True(result.HasGhostColumn);
        Assert.Equal("+1 seat", result.OverflowCaption);
    }

    [Fact]
    public void Compute_MultipleRowsAndSeatsOverCap_ShowsPluralCombinedCaption()
    {
        var result = SeatGridCapping.Compute(rowCount: 10, seatCount: 30);

        Assert.True(result.HasGhostRow);
        Assert.True(result.HasGhostColumn);
        Assert.Equal("+5 rows · +5 seats", result.OverflowCaption);
    }

    [Fact]
    public void Compute_ZeroRowsAndSeats_HasNoGhostRowOrColumn()
    {
        var result = SeatGridCapping.Compute(rowCount: 0, seatCount: 0);

        Assert.Equal(0, result.RowsShown);
        Assert.Equal(0, result.SeatsShown);
        Assert.False(result.HasGhostRow);
        Assert.False(result.HasGhostColumn);
        Assert.Equal("", result.OverflowCaption);
    }

    [Fact]
    public void Compute_WellUnderCap_HasNoGhostRowOrColumn()
    {
        var result = SeatGridCapping.Compute(rowCount: 2, seatCount: 8);

        Assert.Equal(2, result.RowsShown);
        Assert.Equal(8, result.SeatsShown);
        Assert.False(result.HasGhostRow);
        Assert.False(result.HasGhostColumn);
    }
}
