using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using cinescout.model;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace cinescout.core.Discord;

/// <summary>
/// Real implementation: a plain outgoing execute-webhook POST, no bot, no inbound endpoint. Maps a
/// <see cref="NotificationMessage"/> to one Discord embed and applies the channel's options (#139/#141).
/// <c>?wait=true</c> makes Discord answer only once the message is saved — without it a dropped message
/// still returns 204, and NotificationLog would record a success that never happened.
/// </summary>
public sealed class DiscordNotifier(HttpClient httpClient) : IDiscordNotifier
{
    private const int SuppressNotificationsFlag = 1 << 12;
    private const int MaxDetailLength = 1000;

    public async Task<NotificationResult> SendAsync(NotificationChannel channel, NotificationMessage message, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync($"{channel.WebhookUrl}?wait=true", ToPayload(channel, message), cancellationToken);
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                return new NotificationResult(true, $"{status} {response.ReasonPhrase}", HttpStatus: status);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta;
                return new NotificationResult(false, retryAfter is { } delta
                    ? $"Rate limited by Discord, retry after {(int)Math.Ceiling(delta.TotalSeconds)} s"
                    : "Rate limited by Discord", HttpStatus: status);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var detail = Truncate($"{status} {response.ReasonPhrase}: {body}", MaxDetailLength);
            return new NotificationResult(false, detail, WebhookGone: response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized, HttpStatus: status);
        }
        catch (Exception ex) when (ex is TaskCanceledException or TimeoutRejectedException)
        {
            return new NotificationResult(false, "Discord did not answer in time.");
        }
        // HttpRequestException: network failure. InvalidOperationException/UriFormatException: a URL the
        // HttpClient can't use. BrokenCircuitException: the resilience pipeline's breaker is open. All are
        // "never throws" contract violations otherwise, per the doc comment on NotificationResult.
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or UriFormatException or BrokenCircuitException)
        {
            return new NotificationResult(false, Truncate($"Could not reach Discord: {ex.Message}", MaxDetailLength));
        }
    }

    private static WebhookPayload ToPayload(NotificationChannel channel, NotificationMessage message)
    {
        var hasPrefix = !string.IsNullOrWhiteSpace(channel.Prefix);
        var embed = new Embed(
            Truncate(message.Title, 256),
            Truncate(message.Description, 4096),
            message.Color,
            message.Url,
            IsHttpUrl(message.ThumbnailUrl) ? new EmbedImage(message.ThumbnailUrl!) : null,
            message.Fields.Take(25).Select(f => new EmbedField(Truncate(f.Name, 256), Truncate(f.Value, 1024), f.Inline)).ToList(),
            new EmbedFooter(Truncate(message.Footer, 2048)),
            message.Timestamp.ToString("yyyy-MM-ddTHH:mm:sszzz"));

        return new WebhookPayload(
            hasPrefix ? Truncate(channel.Prefix!, 2000) : null,
            string.IsNullOrWhiteSpace(channel.DisplayName) ? null : Truncate(channel.DisplayName, 80),
            string.IsNullOrWhiteSpace(channel.AvatarUrl) ? null : channel.AvatarUrl,
            // Webhooks only parse user mentions by default — an "@here" prefix wouldn't ping without this.
            hasPrefix ? new AllowedMentions(["everyone", "roles", "users"]) : null,
            channel.Suppress ? SuppressNotificationsFlag : null,
            [embed]);
    }

    private static bool IsHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";

    private sealed record WebhookPayload(
        [property: JsonPropertyName("content"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Content,
        [property: JsonPropertyName("username"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Username,
        [property: JsonPropertyName("avatar_url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AvatarUrl,
        [property: JsonPropertyName("allowed_mentions"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AllowedMentions? AllowedMentions,
        [property: JsonPropertyName("flags"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Flags,
        [property: JsonPropertyName("embeds")] IReadOnlyList<Embed> Embeds);

    private sealed record AllowedMentions([property: JsonPropertyName("parse")] IReadOnlyList<string> Parse);

    private sealed record Embed(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("color")] int Color,
        [property: JsonPropertyName("url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url,
        [property: JsonPropertyName("thumbnail"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EmbedImage? Thumbnail,
        [property: JsonPropertyName("fields")] IReadOnlyList<EmbedField> Fields,
        [property: JsonPropertyName("footer")] EmbedFooter Footer,
        [property: JsonPropertyName("timestamp")] string Timestamp);

    private sealed record EmbedImage([property: JsonPropertyName("url")] string Url);

    private sealed record EmbedField(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("value")] string Value,
        [property: JsonPropertyName("inline")] bool Inline);

    private sealed record EmbedFooter([property: JsonPropertyName("text")] string Text);
}
