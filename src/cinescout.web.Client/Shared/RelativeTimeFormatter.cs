namespace cinescout.web.Client.Shared;

/// <summary>
/// §7's "relative activity time" format ("2 hours ago", "1 day ago") as a pure function — first
/// consumer is the Cinemas screen's "Last crawl" caption. Takes the already-UTC instant and "now"
/// as plain parameters rather than calling <c>DateTimeOffset.UtcNow</c> itself, so it stays
/// directly unit-testable with no wall-clock dependency (matching <see cref="SeatGridCapping"/>'s
/// precedent).
/// </summary>
public static class RelativeTimeFormatter
{
    public static string Format(DateTimeOffset? timestamp, DateTimeOffset now)
    {
        if (timestamp is not { } value)
        {
            return "Never";
        }

        var elapsed = now - value;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalSeconds < 60)
        {
            return "just now";
        }

        if (elapsed.TotalMinutes < 60)
        {
            var minutes = (int)elapsed.TotalMinutes;
            return $"{minutes} minute{(minutes == 1 ? "" : "s")} ago";
        }

        if (elapsed.TotalHours < 24)
        {
            var hours = (int)elapsed.TotalHours;
            return $"{hours} hour{(hours == 1 ? "" : "s")} ago";
        }

        var days = (int)elapsed.TotalDays;
        return $"{days} day{(days == 1 ? "" : "s")} ago";
    }
}
