namespace cinescout.contracts;

/// <summary>
/// The Schedule screen's read model (Technical Design Spec.md §5.4, #96): a 14-day, day-grouped
/// window with Previous/Next-week paging. <see cref="WeekOffset"/> echoes back the request's paging
/// state (clamped to &gt;= 0 server-side) so the client can drive its Previous/Next buttons off the
/// response rather than tracking its own copy. Days with zero performances are never present in
/// <see cref="Days"/> — the endpoint only emits groups it actually has performances for, per §5.4's
/// "days with no performances are omitted entirely".
/// </summary>
public sealed record ScheduleDto
{
    /// <summary>§7's week caption, e.g. "Aug 1 – Aug 14 · next 14 days".</summary>
    public required string WeekLabel { get; init; }

    public required int WeekOffset { get; init; }

    public required IReadOnlyList<ScheduleDayDto> Days { get; init; }
}

/// <summary>One day group. <see cref="Label"/> is "Today"/"Tomorrow" at offset 0/1, otherwise a dated heading.</summary>
public sealed record ScheduleDayDto
{
    public required string Label { get; init; }

    public required IReadOnlyList<FilmPerformanceCardModel> Performances { get; init; }
}
