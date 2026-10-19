namespace cinescout.contracts;

/// <summary>
/// The Cinemas screen's detail card + Rooms card (Technical Design Spec.md §5.1), bundled into one
/// response per #83's "one screen, one round trip" principle — selecting a cinema in the
/// master/detail UI always needs both. Nesting <see cref="Rooms"/> (a join over Room) makes this a
/// composed view even though every other field is a direct 1:1 reflection of Cinema, so — per #84's
/// decidable rule — the whole thing stays hand-written in <c>CinemasEndpoints</c> rather than
/// Mapperly-generated.
///
/// <see cref="LastCrawlAt"/> deliberately breaks this project's usual DateTime convention
/// (<see cref="PerformanceDateTimeFormatting"/>'s Kind=Unspecified, Berlin-local rule): it's a
/// genuine UTC instant used only for a relative-time calculation ("2 hours ago"), never displayed
/// as an absolute wall-clock time the way <c>Performance.StartsAt</c> is — so sending it as a true
/// <see cref="DateTimeOffset"/> is correct here, not a bug. The client computes "time ago" against
/// its own <c>DateTimeOffset.UtcNow</c>, which is unambiguous regardless of the browser's local zone.
/// </summary>
public sealed record CinemaDetailDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string ExternalCinemaId { get; init; }
    public required string CrawlBaseUrl { get; init; }
    public string? KinoheldCinemaId { get; init; }
    public required bool IsActive { get; init; }
    public DateTimeOffset? LastCrawlAt { get; init; }
    public required IReadOnlyList<RoomDto> Rooms { get; init; }
}
