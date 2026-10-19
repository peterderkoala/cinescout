using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;

namespace cinescout.core.Discord;

/// <summary>
/// The editable field set of a <see cref="NotificationChannel"/>. WebhookUrl is already validated and
/// normalised by the caller; null means "keep the saved one" (the client only sees it masked).
/// </summary>
public sealed record NotificationChannelInput(
    string Name,
    string? WebhookUrl,
    string? DisplayName,
    string? AvatarUrl,
    string? Prefix,
    bool Suppress,
    bool IsEnabled,
    NotificationTypeFlags Types);

/// <summary>Writes for the Notifications screen, plus "Send test" (#141). Validation is the endpoint's job.</summary>
public sealed class NotificationChannelService(CineScoutDbContext db, IDiscordNotifier notifier)
{
    public async Task<int> CreateAsync(NotificationChannelInput input, CancellationToken cancellationToken)
    {
        var channel = new NotificationChannel
        {
            Name = input.Name,
            WebhookUrl = input.WebhookUrl ?? throw new ArgumentException("A new channel needs a webhook URL.", nameof(input)),
        };
        Apply(channel, input);
        db.NotificationChannels.Add(channel);
        await db.SaveChangesAsync(cancellationToken);
        return channel.Id;
    }

    /// <returns>False if no such channel exists.</returns>
    public async Task<bool> UpdateAsync(int id, NotificationChannelInput input, CancellationToken cancellationToken)
    {
        var channel = await db.NotificationChannels.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (channel is null)
        {
            return false;
        }

        Apply(channel, input);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <returns>False if no such channel exists.</returns>
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken) =>
        await db.NotificationChannels.Where(c => c.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;

    /// <summary>
    /// Sends a sample RulesMatched message through the unsaved options; <paramref name="savedChannelId"/>
    /// supplies the URL when the input carries none. Writes nothing — tests aren't notifications, and a
    /// 401/404 here doesn't switch the saved channel off. Null if the saved channel doesn't exist.
    /// </summary>
    public async Task<NotificationResult?> SendTestAsync(int? savedChannelId, NotificationChannelInput input, CancellationToken cancellationToken)
    {
        var webhookUrl = input.WebhookUrl;
        if (webhookUrl is null)
        {
            webhookUrl = await db.NotificationChannels.Where(c => c.Id == savedChannelId).Select(c => c.WebhookUrl).SingleOrDefaultAsync(cancellationToken);
            if (webhookUrl is null)
            {
                return null;
            }
        }

        var channel = new NotificationChannel { Name = input.Name, WebhookUrl = webhookUrl };
        Apply(channel, input);
        return await notifier.SendAsync(channel, SampleMessage(DateTimeOffset.UtcNow), cancellationToken);
    }

    private static void Apply(NotificationChannel channel, NotificationChannelInput input)
    {
        channel.Name = input.Name.Trim();
        channel.WebhookUrl = input.WebhookUrl ?? channel.WebhookUrl;
        channel.DisplayName = NullIfBlank(input.DisplayName);
        channel.AvatarUrl = NullIfBlank(input.AvatarUrl);
        channel.Prefix = NullIfBlank(input.Prefix);
        channel.Suppress = input.Suppress;
        channel.IsEnabled = input.IsEnabled;
        channel.Types = input.Types;
        channel.LastError = null;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static NotificationMessage SampleMessage(DateTimeOffset now)
    {
        var film = new Film { CinemaId = 0, ExternalFilmId = "sample", Title = "Sample Film" };
        var cinema = new Cinema { ExternalCinemaId = "sample", Name = "Sample Cinema", CrawlBaseUrl = "https://www.kinoheld.de" };
        var room = new Room { ExternalAuditoriumId = "sample", Name = "Saal 1" };
        var performance = new Performance
        {
            SourcePerformanceId = "sample",
            StartsAt = now.AddDays(2),
            BookingLink = "https://www.kinoheld.de",
        };
        var window = new FavoriteTimeWindow { DaysOfWeek = DaysOfWeekFlags.Friday | DaysOfWeekFlags.Saturday, StartTime = new TimeOnly(18, 0), EndTime = new TimeOnly(23, 0) };
        var matrix = new FavoriteSeatMatrix { Name = "Parkett", RowStart = "E", RowEnd = "H", PartySize = 2 };

        return NotificationMessages.RulesMatched(film, cinema, room, performance, window, matrix, freeSeats: 4, cheapestPrice: 12.5m, now)
            with { Footer = "CineScout · test" };
    }
}
