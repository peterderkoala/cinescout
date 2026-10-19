using cinescout.model;

namespace cinescout.core.Discord;

public interface IDiscordNotifier
{
    Task<NotificationResult> SendAsync(NotificationChannel channel, NotificationMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Outcome of one notification attempt — never thrown, always returned, so callers (and their
/// tests) can record it in NotificationLog without needing to catch anything. WebhookGone marks a
/// 401/404: Discord asks clients to stop using that webhook, so the dispatcher switches the channel off.
/// </summary>
public sealed record NotificationResult(bool Success, string Detail, bool WebhookGone = false);
