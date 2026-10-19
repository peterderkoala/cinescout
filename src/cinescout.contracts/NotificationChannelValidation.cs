using System.Text.RegularExpressions;

namespace cinescout.contracts;

/// <summary>
/// Shared by the endpoint and the Notifications screen, like <see cref="CinemaValidation"/>. The webhook
/// URL is an allowlist, not a format check: the server POSTs to whatever is stored, so only Discord's own
/// webhook endpoints are accepted (#141).
/// </summary>
public static partial class NotificationChannelValidation
{
    private static readonly string[] DiscordHosts = ["discord.com", "discordapp.com", "canary.discord.com", "ptb.discord.com"];

    [GeneratedRegex(@"^/api(/v\d+)?/webhooks/\d+/[\w-]+$")]
    private static partial Regex WebhookPath();

    /// <summary>The URL with its host rewritten to discord.com (Discord asks clients to stop using the others), or null if it isn't a Discord webhook URL.</summary>
    public static string? NormalizeWebhookUrl(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || !DiscordHosts.Contains(uri.Host)
            || !WebhookPath().IsMatch(uri.AbsolutePath))
        {
            return null;
        }

        return "https://discord.com" + uri.AbsolutePath;
    }

    public static string? Validate(NotificationChannelWriteRequest request, bool webhookUrlRequired)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Name is required.";
        }

        if (string.IsNullOrWhiteSpace(request.WebhookUrl))
        {
            if (webhookUrlRequired)
            {
                return "Webhook URL is required.";
            }
        }
        else if (NormalizeWebhookUrl(request.WebhookUrl) is null)
        {
            return "Webhook URL must be a Discord webhook URL (https://discord.com/api/webhooks/…).";
        }

        if (!string.IsNullOrWhiteSpace(request.AvatarUrl)
            && (!Uri.TryCreate(request.AvatarUrl, UriKind.Absolute, out var avatar) || avatar.Scheme is not ("http" or "https")))
        {
            return "Avatar URL must be a valid http(s) URL.";
        }

        return null;
    }
}
