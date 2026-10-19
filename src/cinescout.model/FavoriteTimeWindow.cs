namespace cinescout.model;

[Flags]
public enum DaysOfWeekFlags
{
    None = 0,
    Monday = 1 << 0,
    Tuesday = 1 << 1,
    Wednesday = 1 << 2,
    Thursday = 1 << 3,
    Friday = 1 << 4,
    Saturday = 1 << 5,
    Sunday = 1 << 6,
}

/// <summary>
/// A day-of-week + time range the user prefers performances to fall within. Multiple
/// windows can be active at once; a performance matches if it falls in any of them.
/// </summary>
public class FavoriteTimeWindow
{
    public int Id { get; set; }
    public DaysOfWeekFlags DaysOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}
