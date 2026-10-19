using cinescout.core.Discord;
using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace cinescout.core.Tests;

/// <summary>Fan-out and logging semantics from #138; the 401/404 auto-disable from #141.</summary>
public class NotificationDispatcherTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly NotificationMessage Message = new("🆕 New film", "**Vaiana**", 0x9B59B6, [], null, null, DateTimeOffset.UtcNow);

    private async Task<int> SeedFilmAsync(params NotificationChannel[] channels)
    {
        await using var db = new CineScoutDbContext(postgres.Options);
        var cinema = new Cinema { ExternalCinemaId = "580", Name = "Hall of Fame", CrawlBaseUrl = "https://x", IsActive = true };
        db.Cinemas.Add(cinema);
        await db.SaveChangesAsync();
        var film = new Film { CinemaId = cinema.Id, ExternalFilmId = "1", Title = "Vaiana" };
        db.Films.Add(film);
        db.NotificationChannels.AddRange(channels);
        await db.SaveChangesAsync();
        return film.Id;
    }

    private static NotificationChannel Channel(string name, bool enabled = true, NotificationTypeFlags types = NotificationTypeFlags.All) =>
        new() { Name = name, WebhookUrl = $"https://discord.com/api/webhooks/1/{name}", IsEnabled = enabled, Types = types };

    private static IDiscordNotifier Notifier(Func<NotificationChannel, NotificationResult> outcome)
    {
        var notifier = Substitute.For<IDiscordNotifier>();
        notifier.SendAsync(Arg.Any<NotificationChannel>(), Arg.Any<NotificationMessage>(), Arg.Any<CancellationToken>())
            .Returns(call => outcome(call.Arg<NotificationChannel>()));
        return notifier;
    }

    private async Task DispatchAsync(IDiscordNotifier notifier, int filmId, NotificationType type = NotificationType.NewFilmAdded)
    {
        await using var db = new CineScoutDbContext(postgres.Options);
        await NotificationDispatcher.SendAndLogAsync(db, notifier, type, matchId: null, filmId, Message, CancellationToken.None);
    }

    [Fact]
    public async Task Sends_to_every_enabled_subscribed_channel_and_logs_one_row_each_under_one_dispatch()
    {
        var filmId = await SeedFilmAsync(
            Channel("all"),
            Channel("films-only", types: NotificationTypeFlags.NewFilmAdded),
            Channel("seats-only", types: NotificationTypeFlags.SeatAvailabilityChanged | NotificationTypeFlags.RulesMatched),
            Channel("off", enabled: false));
        var notifier = Notifier(_ => new NotificationResult(true, "200 OK"));

        await DispatchAsync(notifier, filmId);

        await using var read = new CineScoutDbContext(postgres.Options);
        var logs = await read.NotificationLogs.OrderBy(l => l.Id).ToListAsync();
        Assert.Equal(["all", "films-only"], logs.Select(l => l.ChannelName));
        Assert.Single(logs.Select(l => l.DispatchId).Distinct());
        Assert.NotEqual(Guid.Empty, logs[0].DispatchId);
        Assert.All(logs, l => Assert.Equal(NotificationStatus.Success, l.Status));

        var channelIds = await read.NotificationChannels.Where(c => c.Name == "all" || c.Name == "films-only").OrderBy(c => c.Id).Select(c => c.Id).ToListAsync();
        Assert.Equal(channelIds, logs.Select(l => l.NotificationChannelId!.Value));
        await notifier.Received(2).SendAsync(Arg.Any<NotificationChannel>(), Message, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task With_no_subscribed_channel_nothing_is_sent_or_logged()
    {
        var filmId = await SeedFilmAsync(Channel("seats-only", types: NotificationTypeFlags.SeatAvailabilityChanged));
        var notifier = Notifier(_ => new NotificationResult(true, "200 OK"));

        await DispatchAsync(notifier, filmId);

        await using var read = new CineScoutDbContext(postgres.Options);
        Assert.Empty(await read.NotificationLogs.ToListAsync());
        await notifier.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default);
    }

    [Fact]
    public async Task One_channel_failing_does_not_stop_the_next()
    {
        var filmId = await SeedFilmAsync(Channel("broken"), Channel("fine"));
        var notifier = Notifier(c => c.Name == "broken" ? new NotificationResult(false, "Rate limited by Discord, retry after 65 s") : new NotificationResult(true, "200 OK"));

        await DispatchAsync(notifier, filmId);

        await using var read = new CineScoutDbContext(postgres.Options);
        var logs = await read.NotificationLogs.OrderBy(l => l.Id).ToListAsync();
        Assert.Equal([NotificationStatus.Failed, NotificationStatus.Success], logs.Select(l => l.Status));
        Assert.Equal("Rate limited by Discord, retry after 65 s", logs[0].ResponseDetail);
        Assert.True((await read.NotificationChannels.SingleAsync(c => c.Name == "broken")).IsEnabled);
    }

    [Fact]
    public async Task A_gone_webhook_switches_its_channel_off_with_a_last_error()
    {
        var filmId = await SeedFilmAsync(Channel("deleted"));
        var notifier = Notifier(_ => new NotificationResult(false, "404 Not Found: {}", WebhookGone: true));

        await DispatchAsync(notifier, filmId);

        await using var read = new CineScoutDbContext(postgres.Options);
        var channel = await read.NotificationChannels.SingleAsync();
        Assert.False(channel.IsEnabled);
        Assert.Equal("Webhook no longer exists. Disabled automatically.", channel.LastError);
        Assert.Equal(NotificationStatus.Failed, (await read.NotificationLogs.SingleAsync()).Status);
    }

    [Fact]
    public async Task Deleting_a_channel_keeps_its_log_rows_with_the_name_snapshot()
    {
        var filmId = await SeedFilmAsync(Channel("gone-soon"));
        await DispatchAsync(Notifier(_ => new NotificationResult(true, "200 OK")), filmId);

        await using (var db = new CineScoutDbContext(postgres.Options))
        {
            db.NotificationChannels.Remove(await db.NotificationChannels.SingleAsync());
            await db.SaveChangesAsync();
        }

        await using var read = new CineScoutDbContext(postgres.Options);
        var log = await read.NotificationLogs.SingleAsync();
        Assert.Null(log.NotificationChannelId);
        Assert.Equal("gone-soon", log.ChannelName);
    }
}
