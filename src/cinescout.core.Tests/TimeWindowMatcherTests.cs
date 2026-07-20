using cinescout.core.Matching;
using cinescout.model;

namespace cinescout.core.Tests;

public class TimeWindowMatcherTests
{
    // Performance.StartsAt is persisted via DateTimeOffset.FromUnixTimeSeconds (UTC offset), but
    // represents a cinema-local (Europe/Berlin) wall-clock instant. Constructing test instants the
    // same way (explicit TimeSpan.Zero offset, UTC hour) — not as pre-converted Berlin offsets —
    // proves the matcher's own Berlin conversion, not just that a no-op conversion round-trips.
    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    private static FavoriteTimeWindow Window(DaysOfWeekFlags days, string start, string end) => new()
    {
        DaysOfWeek = days,
        StartTime = TimeOnly.Parse(start),
        EndTime = TimeOnly.Parse(end),
    };

    [Fact]
    public void Performance_within_a_same_day_window_matches()
    {
        // Friday 2026-07-17 21:00 Berlin (CEST, +2) = 19:00 UTC.
        var startsAt = Utc(2026, 7, 17, 19, 0);
        var window = Window(DaysOfWeekFlags.Friday, "20:00", "23:00");

        var result = TimeWindowMatcher.FindMatchingWindow(startsAt, [window]);

        Assert.Same(window, result);
    }

    [Fact]
    public void Performance_outside_every_window_does_not_match()
    {
        var startsAt = Utc(2026, 7, 17, 19, 0); // Friday 21:00 Berlin
        var window = Window(DaysOfWeekFlags.Friday, "08:00", "12:00");

        var result = TimeWindowMatcher.FindMatchingWindow(startsAt, [window]);

        Assert.Null(result);
    }

    [Fact]
    public void Wrong_day_of_week_does_not_match_even_if_time_of_day_fits()
    {
        var startsAt = Utc(2026, 7, 17, 19, 0); // Friday 21:00 Berlin
        var window = Window(DaysOfWeekFlags.Saturday, "20:00", "23:00");

        var result = TimeWindowMatcher.FindMatchingWindow(startsAt, [window]);

        Assert.Null(result);
    }

    [Fact]
    public void Any_of_multiple_windows_matching_is_sufficient()
    {
        var startsAt = Utc(2026, 7, 17, 19, 0); // Friday 21:00 Berlin
        var nonMatching = Window(DaysOfWeekFlags.Monday, "08:00", "12:00");
        var matching = Window(DaysOfWeekFlags.Friday, "20:00", "23:00");

        var result = TimeWindowMatcher.FindMatchingWindow(startsAt, [nonMatching, matching]);

        Assert.Same(matching, result);
    }

    [Theory]
    [InlineData("20:00")] // exactly StartTime
    [InlineData("23:00")] // exactly EndTime
    public void Window_boundaries_are_inclusive(string time)
    {
        var hourMinute = TimeOnly.Parse(time);
        var startsAt = Utc(2026, 7, 17, hourMinute.Hour - 2, hourMinute.Minute); // Berlin = UTC+2
        var window = Window(DaysOfWeekFlags.Friday, "20:00", "23:00");

        var result = TimeWindowMatcher.FindMatchingWindow(startsAt, [window]);

        Assert.Same(window, result);
    }

    [Fact]
    public void Overnight_window_evening_portion_matches_on_the_flagged_day()
    {
        // Friday 2026-07-17 23:00 Berlin = 21:00 UTC.
        var startsAt = Utc(2026, 7, 17, 21, 0);
        var window = Window(DaysOfWeekFlags.Friday, "22:00", "02:00");

        var result = TimeWindowMatcher.FindMatchingWindow(startsAt, [window]);

        Assert.Same(window, result);
    }

    [Fact]
    public void Overnight_window_early_morning_portion_matches_the_day_after_the_flagged_day()
    {
        // Saturday 2026-07-18 01:00 Berlin = Friday 2026-07-17 23:00 UTC — still belongs to the
        // Friday-flagged overnight window, even though the calendar day is Saturday.
        var startsAt = Utc(2026, 7, 17, 23, 0);
        var window = Window(DaysOfWeekFlags.Friday, "22:00", "02:00");

        var result = TimeWindowMatcher.FindMatchingWindow(startsAt, [window]);

        Assert.Same(window, result);
    }

    [Fact]
    public void Overnight_window_does_not_match_outside_its_wrapped_range()
    {
        // Saturday 2026-07-18 03:00 Berlin = 01:00 UTC — past the 02:00 early-morning cutoff.
        var startsAt = Utc(2026, 7, 18, 1, 0);
        var window = Window(DaysOfWeekFlags.Friday, "22:00", "02:00");

        var result = TimeWindowMatcher.FindMatchingWindow(startsAt, [window]);

        Assert.Null(result);
    }

    [Fact]
    public void Overnight_window_does_not_match_an_unflagged_earlier_day_even_at_a_late_hour()
    {
        // Thursday 2026-07-16 23:00 Berlin = 21:00 UTC — Thursday isn't flagged, only Friday is.
        var startsAt = Utc(2026, 7, 16, 21, 0);
        var window = Window(DaysOfWeekFlags.Friday, "22:00", "02:00");

        var result = TimeWindowMatcher.FindMatchingWindow(startsAt, [window]);

        Assert.Null(result);
    }
}
