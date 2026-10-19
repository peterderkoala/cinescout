using System.Net;
using System.Net.Http.Json;
using cinescout.contracts;
using cinescout.core.Discord;
using cinescout.model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace cinescout.web.Tests;

/// <summary>The Notifications screen's API (#141/#142): CRUD, URL masking and allowlist, and "Send test".</summary>
public sealed class NotificationChannelsEndpointsTests(NotificationChannelsTestFactory factory) : IClassFixture<NotificationChannelsTestFactory>, IAsyncLifetime
{
    private const string Webhook = "https://discord.com/api/webhooks/1290123456789/secret-token";

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        factory.FakeNotifier.Reset();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static NotificationChannelWriteRequest Request(string? webhookUrl = Webhook) => new()
    {
        Name = "seats", WebhookUrl = webhookUrl, DisplayName = "CineScout Seats", Prefix = "@here",
        Suppress = true, IsEnabled = true, Types = NotificationTypeFlags.RulesMatched | NotificationTypeFlags.SeatAvailabilityChanged,
    };

    private async Task<NotificationChannel> StoredAsync(int id)
    {
        await using var db = factory.CreateDbContext();
        return await db.NotificationChannels.SingleAsync(c => c.Id == id);
    }

    private async Task<int> SeedAsync(NotificationChannel channel)
    {
        await using var db = factory.CreateDbContext();
        db.NotificationChannels.Add(channel);
        await db.SaveChangesAsync();
        return channel.Id;
    }

    [Fact]
    public async Task Create_stores_the_channel_with_a_normalised_url_and_returns_it_masked()
    {
        var client = await factory.CreateWritableClientAsync();

        var response = await client.PostAsJsonAsync("/api/notification-channels", Request("https://discordapp.com/api/webhooks/1290123456789/secret-token"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<NotificationChannelDto>())!;
        Assert.Equal("…/webhooks/1290…/••••", dto.WebhookUrlMasked);
        Assert.DoesNotContain("secret-token", await response.Content.ReadAsStringAsync());
        Assert.Equal(NotificationTypeFlags.RulesMatched | NotificationTypeFlags.SeatAvailabilityChanged, dto.Types);

        var stored = await StoredAsync(dto.Id);
        Assert.Equal(Webhook, stored.WebhookUrl);
        Assert.Equal("@here", stored.Prefix);
        Assert.True(stored.Suppress);
    }

    [Fact]
    public async Task List_never_contains_the_token_and_shows_the_last_delivery()
    {
        var id = await SeedAsync(new NotificationChannel { Name = "seats", WebhookUrl = Webhook, LastError = "Webhook no longer exists. Disabled automatically.", IsEnabled = false });
        await using (var db = factory.CreateDbContext())
        {
            db.NotificationLogs.AddRange(
                new NotificationLog { NotificationType = NotificationType.NewFilmAdded, NotificationChannelId = id, ChannelName = "seats", SentAt = DateTimeOffset.UtcNow.AddDays(-2), Status = NotificationStatus.Success },
                new NotificationLog { NotificationType = NotificationType.NewFilmAdded, NotificationChannelId = id, ChannelName = "seats", SentAt = DateTimeOffset.UtcNow.AddHours(-1), Status = NotificationStatus.Failed });
            await db.SaveChangesAsync();
        }

        var client = await factory.CreateAuthenticatedClientAsync();
        var raw = await client.GetStringAsync("/api/notification-channels");
        var dto = Assert.Single((await client.GetFromJsonAsync<List<NotificationChannelDto>>("/api/notification-channels"))!);

        Assert.DoesNotContain("secret-token", raw);
        Assert.Equal("Webhook no longer exists. Disabled automatically.", dto.LastError);
        Assert.Equal(NotificationStatus.Failed, dto.LastDeliveryStatus);
        Assert.True(DateTimeOffset.UtcNow - dto.LastDeliveryAt < TimeSpan.FromHours(2));
    }

    [Fact]
    public async Task Update_without_a_url_keeps_the_stored_one_and_clears_the_last_error()
    {
        var id = await SeedAsync(new NotificationChannel { Name = "old", WebhookUrl = Webhook, IsEnabled = false, LastError = "Webhook no longer exists. Disabled automatically." });
        var client = await factory.CreateWritableClientAsync();

        var response = await client.PutAsJsonAsync($"/api/notification-channels/{id}", Request(webhookUrl: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await StoredAsync(id);
        Assert.Equal(Webhook, stored.WebhookUrl);
        Assert.Equal("seats", stored.Name);
        Assert.True(stored.IsEnabled);
        Assert.Null(stored.LastError);
    }

    [Fact]
    public async Task Update_of_an_unknown_channel_is_404()
    {
        var client = await factory.CreateWritableClientAsync();

        var response = await client.PutAsJsonAsync("/api/notification-channels/999999", Request());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example/api/webhooks/1/token")]
    [InlineData("http://discord.com/api/webhooks/1/token")]
    public async Task A_non_discord_url_is_rejected_with_problem_details(string url)
    {
        var client = await factory.CreateWritableClientAsync();

        var response = await client.PostAsJsonAsync("/api/notification-channels", Request(url));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Webhook URL must be a Discord webhook URL (https://discord.com/api/webhooks/…).", problem!.Detail);
        await using var db = factory.CreateDbContext();
        Assert.Empty(await db.NotificationChannels.ToListAsync());
    }

    [Fact]
    public async Task Delete_removes_the_channel()
    {
        var id = await SeedAsync(new NotificationChannel { Name = "seats", WebhookUrl = Webhook });
        var client = await factory.CreateWritableClientAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/notification-channels/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/notification-channels/{id}")).StatusCode);
    }

    [Fact]
    public async Task Test_sends_a_sample_RulesMatched_embed_with_the_unsaved_options_and_falls_back_to_the_saved_url()
    {
        var id = await SeedAsync(new NotificationChannel { Name = "seats", WebhookUrl = Webhook, DisplayName = "Saved name" });
        factory.FakeNotifier.Next = new NotificationResult(false, "400 Bad Request: {\"message\": \"Invalid Form Body\"}", HttpStatus: 400);
        var client = await factory.CreateWritableClientAsync();

        var response = await client.PostAsJsonAsync($"/api/notification-channels/test?channelId={id}", Request(webhookUrl: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<NotificationTestResultDto>())!;
        Assert.False(result.Success);
        Assert.Equal(400, result.HttpStatus);
        Assert.Equal("400 Bad Request: {\"message\": \"Invalid Form Body\"}", result.Detail);

        var (channel, message) = Assert.Single(factory.FakeNotifier.Calls);
        Assert.Equal(Webhook, channel.WebhookUrl);
        Assert.Equal("CineScout Seats", channel.DisplayName);
        Assert.Equal("@here", channel.Prefix);
        Assert.True(channel.Suppress);
        Assert.Equal("🎬 Matches your preferences", message.Title);
        Assert.Equal("CineScout · test", message.Footer);
        Assert.Contains("Sample Film", message.Description);

        await using var db = factory.CreateDbContext();
        Assert.Empty(await db.NotificationLogs.ToListAsync());
    }

    [Fact]
    public async Task Test_of_a_new_channel_needs_a_url()
    {
        var client = await factory.CreateWritableClientAsync();

        var response = await client.PostAsJsonAsync("/api/notification-channels/test", Request(webhookUrl: ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.FakeNotifier.Calls);
    }

    [Fact]
    public async Task Test_does_not_switch_a_channel_off_when_discord_says_the_webhook_is_gone()
    {
        var id = await SeedAsync(new NotificationChannel { Name = "seats", WebhookUrl = Webhook });
        factory.FakeNotifier.Next = new NotificationResult(false, "404 Not Found: {}", WebhookGone: true);
        var client = await factory.CreateWritableClientAsync();

        await client.PostAsJsonAsync($"/api/notification-channels/test?channelId={id}", Request(webhookUrl: null));

        Assert.True((await StoredAsync(id)).IsEnabled);
    }

    [Fact]
    public async Task Unauthenticated_calls_are_401()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/notification-channels")).StatusCode);
    }
}

public sealed class FakeDiscordNotifier : IDiscordNotifier
{
    public List<(NotificationChannel Channel, NotificationMessage Message)> Calls { get; } = [];

    public NotificationResult Next { get; set; } = new(true, "200 OK");

    public void Reset()
    {
        Calls.Clear();
        Next = new(true, "200 OK");
    }

    public Task<NotificationResult> SendAsync(NotificationChannel channel, NotificationMessage message, CancellationToken cancellationToken)
    {
        Calls.Add((channel, message));
        return Task.FromResult(Next);
    }
}

public sealed class NotificationChannelsTestFactory : PostgresWebFactory
{
    public FakeDiscordNotifier FakeNotifier { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDiscordNotifier>();
            services.AddSingleton<IDiscordNotifier>(FakeNotifier);
        });
    }
}
