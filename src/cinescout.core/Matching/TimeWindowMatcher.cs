using cinescout.model;

namespace cinescout.core.Matching;

/// <summary>
/// Pure logic: does a performance's start time fall within any of the user's FavoriteTimeWindows?
/// No I/O — per #14's testing decision, the matching/rules engine is plain, in-memory logic with
/// no seam. Performance.StartsAt carries a UTC offset (DateTimeOffset.FromUnixTimeSeconds), while
/// FavoriteTimeWindow times are cinema-local wall-clock, so every comparison goes through
/// CinemaTimeZone first.
/// </summary>
public static class TimeWindowMatcher
{
    /// <summary>Returns the first window the performance falls within, or null if none does.</summary>
    public static FavoriteTimeWindow? FindMatchingWindow(DateTimeOffset performanceStartsAt, IEnumerable<FavoriteTimeWindow> windows)
    {
        var local = CinemaTimeZone.ToLocal(performanceStartsAt);
        var day = local.DayOfWeek;
        var previousDay = (DayOfWeek)(((int)day + 6) % 7);
        var time = TimeOnly.FromDateTime(local.DateTime);

        foreach (var window in windows)
        {
            if (MatchesWindow(day, previousDay, time, window))
            {
                return window;
            }
        }

        return null;
    }

    private static bool MatchesWindow(DayOfWeek day, DayOfWeek previousDay, TimeOnly time, FavoriteTimeWindow window)
    {
        if (window.StartTime <= window.EndTime)
        {
            return window.DaysOfWeek.HasFlag(ToFlag(day)) && time >= window.StartTime && time <= window.EndTime;
        }

        // Overnight window (EndTime < StartTime), e.g. Friday 22:00-02:00: covers Friday's evening
        // portion (time >= StartTime) and the following calendar day's early-morning portion
        // (time <= EndTime) — the latter checked against the *previous* flagged day, since a
        // performance at 01:00 on Saturday belongs to a window whose DaysOfWeek names Friday.
        return (window.DaysOfWeek.HasFlag(ToFlag(day)) && time >= window.StartTime)
            || (window.DaysOfWeek.HasFlag(ToFlag(previousDay)) && time <= window.EndTime);
    }

    private static DaysOfWeekFlags ToFlag(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => DaysOfWeekFlags.Monday,
        DayOfWeek.Tuesday => DaysOfWeekFlags.Tuesday,
        DayOfWeek.Wednesday => DaysOfWeekFlags.Wednesday,
        DayOfWeek.Thursday => DaysOfWeekFlags.Thursday,
        DayOfWeek.Friday => DaysOfWeekFlags.Friday,
        DayOfWeek.Saturday => DaysOfWeekFlags.Saturday,
        DayOfWeek.Sunday => DaysOfWeekFlags.Sunday,
        _ => throw new ArgumentOutOfRangeException(nameof(day)),
    };
}
