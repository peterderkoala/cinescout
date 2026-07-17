namespace cinescout.model;

public enum PerformanceStatus
{
    Normal,
    Cancelled,
}

/// <summary>
/// Current-state row for a single bookable showing of a Film. RoomId stays null until the
/// first successful Kinoheld seat crawl resolves it.
/// </summary>
public class Performance
{
    public int Id { get; set; }
    public int FilmId { get; set; }
    public int SiteId { get; set; }
    public int? RoomId { get; set; }
    public required string SourcePerformanceId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public required string BookingLink { get; set; }
    public PerformanceStatus Status { get; set; }
    public bool IsSoldOut { get; set; }
    public bool IsBookable { get; set; }

    /// <summary>
    /// Updated on every Hall-of-Fame crawl this performance appears in. Used to detect
    /// cancellation-by-absence after 2 consecutive misses.
    /// </summary>
    public DateTimeOffset LastSeenAt { get; set; }
}
