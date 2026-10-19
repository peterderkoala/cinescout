using cinescout.model;

namespace cinescout.contracts;

/// <summary>
/// One row of the Notifications screen's channel list. The webhook URL only ever crosses the wire
/// masked — it carries the webhook token (#141). LastDelivery* come from the channel's newest
/// NotificationLog row; <see cref="LastDeliveryAt"/> is UTC like <see cref="RecentActivityDto.SentAt"/>,
/// since it's only ever shown as a relative time.
/// </summary>
public sealed record NotificationChannelDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string WebhookUrlMasked { get; init; }
    public string? DisplayName { get; init; }
    public string? AvatarUrl { get; init; }
    public string? Prefix { get; init; }
    public required bool Suppress { get; init; }
    public required bool IsEnabled { get; init; }
    public required NotificationTypeFlags Types { get; init; }
    public string? LastError { get; init; }
    public NotificationStatus? LastDeliveryStatus { get; init; }
    public DateTimeOffset? LastDeliveryAt { get; init; }
}

/// <summary>Response of <c>POST /api/notification-channels/test</c>: Discord's answer, shown inline next to the button.</summary>
public sealed record NotificationTestResultDto
{
    public required bool Success { get; init; }
    public required string Detail { get; init; }
}
