namespace cinescout.core.Discord;

public interface IDiscordNotifier
{
    Task<NotificationResult> SendAsync(string message, CancellationToken cancellationToken);
}

/// <summary>
/// Outcome of one notification attempt — never thrown, always returned, so callers (and their
/// tests) can record it in NotificationLog without needing to catch anything.
/// </summary>
public sealed record NotificationResult(bool Success, string Detail);
