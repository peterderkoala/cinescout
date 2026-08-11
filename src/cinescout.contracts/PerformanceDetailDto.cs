namespace cinescout.contracts;

/// <summary>
/// The Performance Detail screen's one read model (Technical Design Spec.md §5.7, #98): the
/// featured card plus the current <c>SeatStatus</c> snapshot for <c>LiveSeatGrid</c>. No outcome/
/// alert field here — the four-case Kinoheld alert table is a transient result of the Force Refresh
/// action, not persisted page state (mirrors <c>CinemasApiClient.ReseedRoomsAsync</c>'s
/// <c>ApiProblem.KinoheldStatus</c> pattern rather than a field on this DTO).
/// </summary>
public sealed record PerformanceDetailDto
{
    public required FilmPerformanceCardModel Card { get; init; }
    public required IReadOnlyList<LiveSeatDto> Seats { get; init; }
}
