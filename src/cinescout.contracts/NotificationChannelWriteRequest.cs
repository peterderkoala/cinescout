using cinescout.model;

namespace cinescout.contracts;

/// <summary>
/// Request body for <c>POST /api/notification-channels</c>, <c>PUT /api/notification-channels/{id}</c>
/// and <c>POST /api/notification-channels/test</c>. A null/blank <see cref="WebhookUrl"/> on PUT or test
/// means "keep the saved one" — the client only ever sees it masked (#141).
/// </summary>
public sealed record NotificationChannelWriteRequest
{
    public required string Name { get; init; }
    public string? WebhookUrl { get; init; }
    public string? DisplayName { get; init; }
    public string? AvatarUrl { get; init; }
    public string? Prefix { get; init; }
    public required bool Suppress { get; init; }
    public required bool IsEnabled { get; init; }
    public required NotificationTypeFlags Types { get; init; }
}
