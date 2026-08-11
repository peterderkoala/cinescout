namespace cinescout.contracts;

/// <summary>
/// The Tracked Movies screen's one read model (Technical Design Spec.md §5.5, #83's "one screen,
/// one round trip" convention, #95): every tracked film with its full upcoming-performance list
/// (compact cards), plus the "All films" list of films with at least one upcoming performance and
/// no <c>TrackedMovie</c> row. Track/Untrack (#95) return this same shape after mutating, since a
/// single action moves a film between both lists.
/// </summary>
public sealed record TrackedMoviesPageDto
{
    public required IReadOnlyList<TrackedMovieRowDto> TrackedMovies { get; init; }
    public required IReadOnlyList<UntrackedFilmDto> AllFilms { get; init; }
}

/// <summary>
/// One tracked film's block: title (for the empty-state line, which needs it even when
/// <see cref="Performances"/> is empty) plus every upcoming, non-cancelled performance as a compact
/// <see cref="FilmPerformanceCardModel"/> — no cap, unlike Home's featured/tracked panels, since the
/// ticket states no limit for this screen.
/// </summary>
public sealed record TrackedMovieRowDto
{
    public required int FilmId { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<FilmPerformanceCardModel> Performances { get; init; }
}

/// <summary>One "All films" row — a film eligible to track (has an upcoming performance, not already tracked).</summary>
public sealed record UntrackedFilmDto
{
    public required int FilmId { get; init; }
    public required string Title { get; init; }
}
