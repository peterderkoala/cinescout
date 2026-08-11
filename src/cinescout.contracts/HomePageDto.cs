namespace cinescout.contracts;

/// <summary>
/// The Home screen's one read model (Technical Design Spec.md §5.6, #83's "one screen, one round
/// trip" convention): featured <c>Match</c> cards, the Tracked Movies panel (max 5), and Recent
/// Activity (last 5 <c>NotificationLog</c> rows). <see cref="HasAnyTrackedMovie"/> is separate from
/// <see cref="TrackedMovies"/>.Count because the panel is capped at 5 — it's what picks between the
/// two empty-state copy variants when <see cref="ActiveMatches"/> is empty, and needs the *true*
/// existence of any tracked film, not just whether the capped list happens to be non-empty.
/// </summary>
public sealed record HomePageDto
{
    public required IReadOnlyList<FilmPerformanceCardModel> ActiveMatches { get; init; }
    public required bool HasAnyTrackedMovie { get; init; }
    public required IReadOnlyList<TrackedMovieHomeRowDto> TrackedMovies { get; init; }
    public required IReadOnlyList<RecentActivityDto> RecentActivity { get; init; }
}

/// <summary>
/// One Tracked Movies panel row. <see cref="NextPerformance"/> is null when the film has no
/// upcoming performance — the design's "No upcoming performances" row (§5.6) — in which case
/// <see cref="FilmTitle"/> is still needed since there's no card to read a title from.
/// </summary>
public sealed record TrackedMovieHomeRowDto
{
    public required string FilmTitle { get; init; }
    public FilmPerformanceCardModel? NextPerformance { get; init; }
}

/// <summary>
/// One Recent Activity row (§5.6: <c>{Film.Title}</c> + relative <c>SentAt</c>). <see cref="SentAt"/>
/// crosses the wire as a genuine UTC <see cref="DateTimeOffset"/>, the same deliberate exception
/// <see cref="CinemaDetailDto.LastCrawlAt"/> documents: it's used only for a relative-time
/// calculation, never displayed as an absolute wall-clock time, so the client formats "time ago"
/// against its own <c>DateTimeOffset.UtcNow</c> via <c>cinescout.web.Client.Shared.RelativeTimeFormatter</c>
/// rather than this DTO carrying a pre-formatted, already-stale-by-the-time-it-renders string.
/// </summary>
public sealed record RecentActivityDto
{
    public required string FilmTitle { get; init; }
    public required DateTimeOffset SentAt { get; init; }
}
