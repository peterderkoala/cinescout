namespace cinescout.model;

/// <summary>Bit per <see cref="NotificationType"/> (bit index = enum value), the set a <see cref="NotificationChannel"/> subscribes to.</summary>
[Flags]
public enum NotificationTypeFlags
{
    None = 0,
    TrackStarted = 1 << (int)NotificationType.TrackStarted,
    TrackStopped = 1 << (int)NotificationType.TrackStopped,
    RulesMatched = 1 << (int)NotificationType.RulesMatched,
    SeatAvailabilityChanged = 1 << (int)NotificationType.SeatAvailabilityChanged,
    NewFilmAdded = 1 << (int)NotificationType.NewFilmAdded,
    All = TrackStarted | TrackStopped | RulesMatched | SeatAvailabilityChanged | NewFilmAdded,
}

public static class NotificationTypeFlagsExtensions
{
    public static NotificationTypeFlags ToFlag(this NotificationType type) => (NotificationTypeFlags)(1 << (int)type);
}

/// <summary>
/// A delivery target the user configured — today always a Discord webhook. A notification goes out
/// through every enabled channel whose <see cref="Types"/> include its type (#138). LastError is set
/// when Discord reports the webhook gone (401/404) and the channel was switched off automatically;
/// saving or re-enabling the channel clears it (#141).
/// </summary>
public class NotificationChannel
{
    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Full webhook URL including its token; host normalised to discord.com. Never sent to the client unmasked.</summary>
    public required string WebhookUrl { get; set; }

    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }

    /// <summary>Plain-text message content posted above the embed (e.g. "@here").</summary>
    public string? Prefix { get; set; }

    /// <summary>Discord's SUPPRESS_NOTIFICATIONS flag: the message posts without push/desktop alerts.</summary>
    public bool Suppress { get; set; }

    public bool IsEnabled { get; set; } = true;
    public NotificationTypeFlags Types { get; set; } = NotificationTypeFlags.All;
    public string? LastError { get; set; }
}
