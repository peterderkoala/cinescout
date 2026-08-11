namespace cinescout.contracts;

/// <summary>
/// The row/seat-number geometry of one <c>cinescout.model.FavoriteSeatMatrix</c> — <c>ZoneSeatGrid</c>'s
/// entire input (Technical Design Spec.md §4.2's "zone" mode). Deliberately narrower than the model:
/// no <c>Id</c>/<c>RoomId</c>/<c>FilmId</c>/<c>Name</c>/<c>PartySize</c>/<c>IsEnabled</c> — the grid
/// only ever renders the four bounds below. <see cref="RowStart"/>/<see cref="RowEnd"/> stay plain
/// strings (Kinoheld rows are single letters, compared lexicographically), matching the model.
/// </summary>
public sealed record ZoneSeatGridModel
{
    public required string RowStart { get; init; }
    public required string RowEnd { get; init; }
    public required int SeatNumberStart { get; init; }
    public required int SeatNumberEnd { get; init; }
}
