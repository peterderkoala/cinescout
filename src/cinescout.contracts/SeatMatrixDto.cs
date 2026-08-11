namespace cinescout.contracts;

/// <summary>Read model for a <c>FavoriteSeatMatrix</c> row (§5.2) — a direct 1:1 reflection of the model, mapped by <c>SeatMatrixMapper</c>.</summary>
public sealed record SeatMatrixDto
{
    public required int Id { get; init; }
    public required int RoomId { get; init; }
    public required int? FilmId { get; init; }
    public required string Name { get; init; }
    public required string RowStart { get; init; }
    public required string RowEnd { get; init; }
    public required int SeatNumberStart { get; init; }
    public required int SeatNumberEnd { get; init; }
    public required int PartySize { get; init; }
    public required bool IsEnabled { get; init; }
}
