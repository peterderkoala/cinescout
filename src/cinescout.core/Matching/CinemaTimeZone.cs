namespace cinescout.core.Matching;

/// <summary>
/// CineScout crawls a single, hardcoded German site today (see CONTEXT.md's Site glossary entry),
/// so a single hardcoded IANA zone id is the deliberate simplification, matching the rest of this
/// codebase's single-site posture — not something to generalize until a second, differently-zoned
/// Site is actually configured. <see cref="cinescout.model.Performance.StartsAt"/> is persisted via
/// <c>DateTimeOffset.FromUnixTimeSeconds</c> (a UTC-offset instant); <see cref="cinescout.model.FavoriteTimeWindow"/>
/// times are cinema-local wall-clock, so every comparison between the two must go through this conversion.
/// </summary>
public static class CinemaTimeZone
{
    public static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public static DateTimeOffset ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Berlin);
}
