namespace cinescout.model;

public enum NotificationType
{
    TrackStarted,
    TrackStopped,
    RulesMatched,
    SeatAvailabilityChanged,
    NewFilmAdded,
}

public enum NotificationStatus
{
    Success,
    Failed,
}

/// <summary>
/// A record of one attempt to notify the user. MatchId is only set for RulesMatched and
/// SeatAvailabilityChanged — the track-lifecycle types aren't tied to a specific Match. FilmId is
/// set for every notification type (the Home screen's Recent Activity panel needs a title to show
/// regardless of type — see §5.6) — for RulesMatched/SeatAvailabilityChanged it's redundant with
/// MatchId's Performance.FilmId, but storing it directly avoids Recent Activity needing a
/// Match→Performance join just to read a title.
/// </summary>
public class NotificationLog
{
    public int Id { get; set; }
    public NotificationType NotificationType { get; set; }
    public int? MatchId { get; set; }
    public int? FilmId { get; set; }
    public required string Channel { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public NotificationStatus Status { get; set; }
    public string? ResponseDetail { get; set; }
}
