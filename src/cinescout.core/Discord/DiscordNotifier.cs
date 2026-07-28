using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Polly.Timeout;

namespace cinescout.core.Discord;

/// <summary>
/// Real implementation: a plain outgoing webhook POST, no bot, no inbound endpoint. The webhook
/// URL (read from config at call time, not baked into HttpClient.BaseAddress at DI-registration
/// time) is the full Discord-provided endpoint, already including the webhook id/token.
/// </summary>
public sealed class DiscordNotifier(HttpClient httpClient, IConfiguration configuration) : IDiscordNotifier
{
    public async Task<NotificationResult> SendAsync(string message, CancellationToken cancellationToken)
    {
        var webhookUrl = configuration["Discord:WebhookUrl"];
        if (string.IsNullOrEmpty(webhookUrl))
        {
            return new NotificationResult(false, "Discord webhook URL not configured.");
        }

        try
        {
            var response = await httpClient.PostAsJsonAsync(webhookUrl, new { content = message }, cancellationToken);
            return new NotificationResult(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.ReasonPhrase}");
        }
        // InvalidOperationException: a malformed (not merely empty) webhook URL — PostAsJsonAsync
        // throws before any request is attempted. TaskCanceledException/TimeoutRejectedException:
        // the AddStandardResilienceHandler pipeline's attempt/total timeout tripping. All three are
        // "never throws" contract violations otherwise, per the doc comment on IDiscordNotifier.
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException or TimeoutRejectedException)
        {
            return new NotificationResult(false, ex.Message);
        }
    }
}
