namespace cinescout.contracts;

/// <summary>
/// Request body for <c>POST /api/seat-matrices</c> and <c>PUT /api/seat-matrices/{id}</c> (§5.2's
/// editor card). No <c>IsEnabled</c> field — the editor has no control for it; new matrices default
/// to enabled, and it's toggled from the card instead (<c>POST /api/seat-matrices/{id}/toggle</c>).
/// </summary>
public sealed record SeatMatrixWriteRequest
{
    public required int RoomId { get; init; }
    public int? FilmId { get; init; }
    public required string Name { get; init; }
    public required string RowStart { get; init; }
    public required string RowEnd { get; init; }
    public required int SeatNumberStart { get; init; }
    public required int SeatNumberEnd { get; init; }
    public required int PartySize { get; init; }
}
