using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;

namespace cinescout.core.Discord;

/// <summary>
/// Shared fan-out-then-log shape used by every notification call site (track-lifecycle, match
/// events, new-film-added), per #138: send to every enabled channel subscribed to the type, one after
/// another so one failure never stops the rest, and write one NotificationLog row per attempt —
/// success or failure, since a failed delivery is itself worth an audit trail, not a reason to roll
/// back whatever state change already committed. No subscribed channel means no row: subscribing
/// nothing is a choice, not a failure.
/// </summary>
public static class NotificationDispatcher
{
    public const string WebhookGoneError = "Webhook no longer exists. Disabled automatically.";

    public static async Task<IReadOnlyList<NotificationResult>> SendAndLogAsync(
        CineScoutDbContext db,
        IDiscordNotifier notifier,
        NotificationType type,
        int? matchId,
        int filmId,
        NotificationMessage message,
        CancellationToken cancellationToken)
    {
        var flag = type.ToFlag();
        var channels = await db.NotificationChannels
            .Where(c => c.IsEnabled && (c.Types & flag) == flag)
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken);

        var dispatchId = Guid.NewGuid();
        var results = new List<NotificationResult>(channels.Count);
        foreach (var channel in channels)
        {
            var result = await notifier.SendAsync(channel, message, cancellationToken);
            results.Add(result);

            db.NotificationLogs.Add(new NotificationLog
            {
                NotificationType = type,
                MatchId = matchId,
                FilmId = filmId,
                NotificationChannelId = channel.Id,
                ChannelName = channel.Name,
                DispatchId = dispatchId,
                SentAt = DateTimeOffset.UtcNow,
                Status = result.Success ? NotificationStatus.Success : NotificationStatus.Failed,
                ResponseDetail = result.Detail,
            });

            // Discord asks clients to stop calling a deleted webhook (#137); the screen shows why it stopped.
            if (result.WebhookGone)
            {
                channel.IsEnabled = false;
                channel.LastError = WebhookGoneError;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return results;
    }
}
