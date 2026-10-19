namespace cinescout.core.Discord;

/// <summary>
/// One notification's content, Discord-agnostic: <see cref="DiscordNotifier"/> alone maps it to an embed
/// (and applies the channel's display name, avatar, prefix and suppress options) — #139.
/// </summary>
public sealed record NotificationMessage(
    string Title,
    string Description,
    int Color,
    IReadOnlyList<NotificationField> Fields,
    string? Url,
    string? ThumbnailUrl,
    DateTimeOffset Timestamp,
    string Footer = "CineScout");

public sealed record NotificationField(string Name, string Value, bool Inline = false);
