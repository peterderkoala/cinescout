namespace cinescout.model;

public enum NotificationType
{
    WatchStarted,
    WatchStopped,
    RulesMatched,
    SeatAvailabilityChanged,
}

public enum NotificationStatus
{
    Success,
    Failed,
}

/// <summary>
/// A record of one attempt to notify the user. MatchId is only set for RulesMatched and
/// SeatAvailabilityChanged — the watch-lifecycle types aren't tied to a specific Match.
/// </summary>
public class NotificationLog
{
    public int Id { get; set; }
    public NotificationType NotificationType { get; set; }
    public int? MatchId { get; set; }
    public required string Channel { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public NotificationStatus Status { get; set; }
    public string? ResponseDetail { get; set; }
}
