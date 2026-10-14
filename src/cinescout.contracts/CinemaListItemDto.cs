namespace cinescout.contracts;

/// <summary>
/// One row of the Cinemas screen's master list (Technical Design Spec.md §5.1), ordered by Name.
/// <see cref="RoomCount"/> is a join/aggregate over Room, not a raw Cinema field, so this DTO is a
/// composed view and stays hand-written in <c>CinemasEndpoints</c> rather than Mapperly-generated
/// (#84's decidable rule). The "{n} rooms" / "No rooms" text itself is formatted client-side from
/// the raw count, the same way <c>LiveSeatGrid</c> formats its "{free} of {total}" caption.
/// </summary>
public sealed record CinemaListItemDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string ExternalCinemaId { get; init; }
    public required bool IsActive { get; init; }
    public required int RoomCount { get; init; }
}
