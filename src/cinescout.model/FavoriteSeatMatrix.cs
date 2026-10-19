namespace cinescout.model;

/// <summary>
/// A user-defined zone within a Room — a row range plus a seat-number range plus a
/// PartySize — used to check whether enough adjacent seats are free for the user's group.
/// FilmId null means a general matrix for the room; a non-null FilmId is a film-specific
/// override that takes precedence over the general matrix. Also subsumes "liking a room
/// in general" (a wide-open matrix), so there is no separate FavoriteRoom entity.
/// </summary>
public class FavoriteSeatMatrix
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public int? FilmId { get; set; }
    public required string Name { get; set; }

    /// <summary>Plain strings, compared lexicographically — Kinoheld rows are single letters.</summary>
    public required string RowStart { get; set; }
    public required string RowEnd { get; set; }
    public int SeatNumberStart { get; set; }
    public int SeatNumberEnd { get; set; }
    public int PartySize { get; set; }
    public bool IsEnabled { get; set; }
}
