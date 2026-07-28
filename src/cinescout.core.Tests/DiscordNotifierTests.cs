using cinescout.core.Discord;
using Microsoft.Extensions.Configuration;
using Polly.Timeout;

namespace cinescout.core.Tests;

/// <summary>
/// Covers #58: <see cref="DiscordNotifier"/>'s doc comment promises "never throws" — every path
/// below must come back as a failed <see cref="NotificationResult"/> instead of propagating.
/// </summary>
public sealed class DiscordNotifierTests
{
    [Fact]
    public async Task Send_with_no_webhook_configured_returns_not_configured_without_attempting_a_request()
    {
        var configuration = new ConfigurationBuilder().Build();
        var notifier = new DiscordNotifier(new HttpClient(new ThrowingHandler(new InvalidOperationException("should not be reached"))), configuration);

        var result = await notifier.SendAsync("hello", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Discord webhook URL not configured.", result.Detail);
    }

    [Fact]
    public async Task Send_with_a_malformed_webhook_url_returns_a_failed_result_instead_of_throwing()
    {
        // Not absolute and the HttpClient has no BaseAddress set, so PostAsJsonAsync throws
        // InvalidOperationException before any request is attempted.
        var configuration = ConfiguredWith("not-a-valid-url");
        var notifier = new DiscordNotifier(new HttpClient(), configuration);

        var result = await notifier.SendAsync("hello", CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Send_when_the_underlying_request_throws_HttpRequestException_returns_a_failed_result()
    {
        var configuration = ConfiguredWith("https://discord.example/webhook");
        var notifier = new DiscordNotifier(new HttpClient(new ThrowingHandler(new HttpRequestException("connection refused"))), configuration);

        var result = await notifier.SendAsync("hello", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("connection refused", result.Detail);
    }

    [Fact]
    public async Task Send_when_the_resilience_pipeline_cancels_the_attempt_returns_a_failed_result()
    {
        var configuration = ConfiguredWith("https://discord.example/webhook");
        var notifier = new DiscordNotifier(new HttpClient(new ThrowingHandler(new TaskCanceledException("attempt timed out"))), configuration);

        var result = await notifier.SendAsync("hello", CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Send_when_the_resilience_pipeline_rejects_on_timeout_returns_a_failed_result()
    {
        var configuration = ConfiguredWith("https://discord.example/webhook");
        var notifier = new DiscordNotifier(new HttpClient(new ThrowingHandler(new TimeoutRejectedException("total request timeout exceeded"))), configuration);

        var result = await notifier.SendAsync("hello", CancellationToken.None);

        Assert.False(result.Success);
    }

    private static IConfiguration ConfiguredWith(string webhookUrl) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Discord:WebhookUrl", webhookUrl)])
            .Build();

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw exception;
    }
}
