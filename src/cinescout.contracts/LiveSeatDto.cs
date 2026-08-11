namespace cinescout.contracts;

/// <summary>
/// One <c>cinescout.model.SeatStatus</c> row for <c>LiveSeatGrid</c> (Technical Design Spec.md
/// §4.2's "live" mode) — a true leaf DTO, not a 1:1 Mapperly shape, since cinescout.contracts can
/// never reference cinescout.model. Carries exactly what the grid needs to lay out and style one
/// cell: <see cref="SourceSeatId"/>/<see cref="LeftNeighborSeatId"/>/<see cref="RightNeighborSeatId"/>
/// so <c>LiveSeatGridLayout</c> can walk true physical adjacency the same way
/// <c>cinescout.core.Matching.SeatBlockFinder</c> does, rather than trusting raw
/// <see cref="SeatNumber"/> gaps to mean "no seat here" (issue #77's resolution).
/// </summary>
public sealed record LiveSeatDto
{
    public required string SourceSeatId { get; init; }
    public required string Row { get; init; }
    public required int SeatNumber { get; init; }
    public required SeatOccupancyStatus Status { get; init; }
    public string? LeftNeighborSeatId { get; init; }
    public string? RightNeighborSeatId { get; init; }
}
