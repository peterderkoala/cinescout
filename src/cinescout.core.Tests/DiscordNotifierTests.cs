using System.Net;
using System.Text.Json;
using cinescout.core.Discord;
using cinescout.core.Extensions;
using cinescout.model;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace cinescout.core.Tests;

/// <summary>
/// The HTTP edge (#141/#139): the Discord execute-webhook payload, how each response maps to a
/// <see cref="NotificationResult"/>, and the "never throws" contract on <see cref="IDiscordNotifier"/>.
/// </summary>
public sealed class DiscordNotifierTests
{
    private const string Webhook = "https://discord.com/api/webhooks/1/token";

    private static NotificationChannel Channel() => new() { Name = "seats", WebhookUrl = Webhook };

    private static NotificationMessage Message() => new(
        "🎬 Matches your preferences", "**Vaiana**", 0x2ECC71,
        [new NotificationField("Cinema", "Hall of Fame", Inline: true), new NotificationField("Seats", "4 free")],
        "https://kinoheld.de/book/7", "https://img.example/vaiana.jpg",
        new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero));

    private static async Task<(NotificationResult Result, RecordingHandler Handler)> SendAsync(
        NotificationChannel channel, NotificationMessage message, HttpResponseMessage? response = null)
    {
        var handler = new RecordingHandler(response ?? new HttpResponseMessage(HttpStatusCode.OK));
        var result = await new DiscordNotifier(new HttpClient(handler)).SendAsync(channel, message, CancellationToken.None);
        return (result, handler);
    }

    [Fact]
    public async Task Posts_one_embed_to_the_webhook_with_wait_true()
    {
        var (result, handler) = await SendAsync(Channel(), Message());

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(Webhook + "?wait=true", handler.Uri!.ToString());

        var body = handler.Body!.RootElement;
        var embed = Assert.Single(body.GetProperty("embeds").EnumerateArray());
        Assert.Equal("🎬 Matches your preferences", embed.GetProperty("title").GetString());
        Assert.Equal("**Vaiana**", embed.GetProperty("description").GetString());
        Assert.Equal(0x2ECC71, embed.GetProperty("color").GetInt32());
        Assert.Equal("https://kinoheld.de/book/7", embed.GetProperty("url").GetString());
        Assert.Equal("https://img.example/vaiana.jpg", embed.GetProperty("thumbnail").GetProperty("url").GetString());
        Assert.Equal("CineScout", embed.GetProperty("footer").GetProperty("text").GetString());
        Assert.Equal("2026-10-19T08:00:00+00:00", embed.GetProperty("timestamp").GetString());

        var fields = embed.GetProperty("fields").EnumerateArray().ToList();
        Assert.Equal("Cinema", fields[0].GetProperty("name").GetString());
        Assert.Equal("Hall of Fame", fields[0].GetProperty("value").GetString());
        Assert.True(fields[0].GetProperty("inline").GetBoolean());
        Assert.False(fields[1].GetProperty("inline").GetBoolean());
    }

    [Fact]
    public async Task Unset_channel_options_are_left_out_of_the_payload()
    {
        var (_, handler) = await SendAsync(Channel(), Message() with { Url = null, ThumbnailUrl = null });

        var body = handler.Body!.RootElement;
        foreach (var absent in new[] { "content", "username", "avatar_url", "allowed_mentions", "flags" })
        {
            Assert.False(body.TryGetProperty(absent, out _), absent);
        }

        var embed = body.GetProperty("embeds")[0];
        Assert.False(embed.TryGetProperty("url", out _));
        Assert.False(embed.TryGetProperty("thumbnail", out _));
    }

    [Fact]
    public async Task Channel_options_map_to_username_avatar_prefix_mentions_and_suppress_flag()
    {
        var channel = Channel();
        channel.DisplayName = "CineScout Seats";
        channel.AvatarUrl = "https://img.example/avatar.png";
        channel.Prefix = "@here";
        channel.Suppress = true;

        var (_, handler) = await SendAsync(channel, Message());

        var body = handler.Body!.RootElement;
        Assert.Equal("CineScout Seats", body.GetProperty("username").GetString());
        Assert.Equal("https://img.example/avatar.png", body.GetProperty("avatar_url").GetString());
        Assert.Equal("@here", body.GetProperty("content").GetString());
        Assert.Equal(["everyone", "roles", "users"],
            body.GetProperty("allowed_mentions").GetProperty("parse").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(4096, body.GetProperty("flags").GetInt32());
    }

    [Fact]
    public async Task A_thumbnail_that_is_not_an_absolute_http_url_is_dropped()
    {
        var (_, handler) = await SendAsync(Channel(), Message() with { ThumbnailUrl = "/posters/vaiana.jpg" });

        Assert.False(handler.Body!.RootElement.GetProperty("embeds")[0].TryGetProperty("thumbnail", out _));
    }

    [Fact]
    public async Task Values_over_Discord_limits_are_truncated_with_an_ellipsis()
    {
        var fields = Enumerable.Range(0, 30).Select(i => new NotificationField(new string('n', 300), new string('v', 1100))).ToList();
        var message = Message() with { Title = new string('t', 300), Description = new string('d', 5000), Fields = fields };

        var (_, handler) = await SendAsync(Channel(), message);

        var embed = handler.Body!.RootElement.GetProperty("embeds")[0];
        var title = embed.GetProperty("title").GetString()!;
        Assert.Equal(256, title.Length);
        Assert.EndsWith("…", title);
        Assert.Equal(4096, embed.GetProperty("description").GetString()!.Length);
        Assert.Equal(25, embed.GetProperty("fields").GetArrayLength());
        Assert.Equal(256, embed.GetProperty("fields")[0].GetProperty("name").GetString()!.Length);
        Assert.Equal(1024, embed.GetProperty("fields")[0].GetProperty("value").GetString()!.Length);
    }

    [Fact]
    public async Task A_429_fails_with_the_retry_after_hint()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(65));

        var (result, _) = await SendAsync(Channel(), Message(), response);

        Assert.False(result.Success);
        Assert.False(result.WebhookGone);
        Assert.Equal(429, result.HttpStatus);
        Assert.Equal("Rate limited by Discord, retry after 65 s", result.Detail);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_401_or_404_reports_the_webhook_gone(HttpStatusCode status)
    {
        var (result, _) = await SendAsync(Channel(), Message(), new HttpResponseMessage(status) { Content = new StringContent("""{"message": "Unknown Webhook", "code": 10015}""") });

        Assert.False(result.Success);
        Assert.True(result.WebhookGone);
    }

    [Fact]
    public async Task A_400_carries_Discords_error_body_capped_at_1000_characters()
    {
        var shortBody = """{"message": "Invalid Form Body", "code": 50035}""";
        var (result, _) = await SendAsync(Channel(), Message(), new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(shortBody) });

        Assert.False(result.Success);
        Assert.Equal(400, result.HttpStatus);
        Assert.Equal("400 Bad Request: " + shortBody, result.Detail);

        var (longResult, _) = await SendAsync(Channel(), Message(), new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(new string('x', 2000)) });
        Assert.Equal(1000, longResult.Detail.Length);
    }

    public static TheoryData<Exception> ThrownByTheHttpStack() =>
    [
        new HttpRequestException("connection refused"),
        new TaskCanceledException("attempt timed out"),
        new TimeoutRejectedException("total request timeout exceeded"),
        new BrokenCircuitException("circuit open"),
        new UriFormatException("bad uri"),
        new InvalidOperationException("no base address"),
    ];

    [Theory]
    [MemberData(nameof(ThrownByTheHttpStack))]
    public async Task Never_throws_when_the_http_stack_does(Exception exception)
    {
        var notifier = new DiscordNotifier(new HttpClient(new ThrowingHandler(exception)));

        var result = await notifier.SendAsync(Channel(), Message(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.WebhookGone);
        Assert.Null(result.HttpStatus);
    }

    [Fact]
    public async Task The_registered_client_never_retries_a_failed_POST()
    {
        var handler = new CountingHandler(HttpStatusCode.InternalServerError);
        var services = new ServiceCollection().AddDiscord();
        services.AddHttpClient<IDiscordNotifier, DiscordNotifier>().ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IDiscordNotifier>().SendAsync(Channel(), Message(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(1, handler.Calls);
    }

    private sealed class CountingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public JsonDocument? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            Body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            return response;
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw exception;
    }
}
