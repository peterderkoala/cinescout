namespace cinescout.web.Client.Shared;

/// <summary>
/// The §4.2 zone-capping rule as pure, no-Blazor logic (issue #77's resolution) — never draw more
/// than <see cref="MaxRows"/> rows × <see cref="MaxSeatsPerRow"/> seats, so every card in a grid is
/// the same height regardless of auditorium size. Overflow becomes one 35%-opacity ghost row/column
/// (rendered by <c>ZoneSeatGrid.razor</c>, not here) plus a singular/plural <c>+N rows · +N seats</c>
/// caption.
/// </summary>
public static class SeatGridCapping
{
    public const int MaxRows = 5;
    public const int MaxSeatsPerRow = 25;

    public static SeatGridCappingResult Compute(int rowCount, int seatCount)
    {
        var rowsShown = Math.Min(rowCount, MaxRows);
        var seatsShown = Math.Min(seatCount, MaxSeatsPerRow);
        var extraRows = rowCount - rowsShown;
        var extraSeats = seatCount - seatsShown;

        var parts = new List<string>();
        if (extraRows > 0)
        {
            parts.Add($"+{extraRows} row{(extraRows > 1 ? "s" : "")}");
        }
        if (extraSeats > 0)
        {
            parts.Add($"+{extraSeats} seat{(extraSeats > 1 ? "s" : "")}");
        }

        return new SeatGridCappingResult(
            rowsShown,
            seatsShown,
            HasGhostRow: extraRows > 0,
            HasGhostColumn: extraSeats > 0,
            OverflowCaption: string.Join(" · ", parts));
    }
}

public sealed record SeatGridCappingResult(
    int RowsShown,
    int SeatsShown,
    bool HasGhostRow,
    bool HasGhostColumn,
    string OverflowCaption);
