using cinescout.model;
using cinescout.persistence;

namespace cinescout.core.Discord;

/// <summary>
/// Shared send-then-log shape used by every notification call site (track-lifecycle, match
/// events, new-film-added): send via IDiscordNotifier, then write a NotificationLog row
/// unconditionally — success or failure, since a failed Discord delivery is itself worth an
/// audit trail, not a reason to roll back whatever state change already committed.
/// </summary>
public static class NotificationDispatcher
{
    public static async Task<NotificationResult> SendAndLogAsync(
        CineScoutDbContext db,
        IDiscordNotifier notifier,
        NotificationType type,
        int? matchId,
        string message,
        CancellationToken cancellationToken)
    {
        var result = await notifier.SendAsync(message, cancellationToken);

        db.NotificationLogs.Add(new NotificationLog
        {
            NotificationType = type,
            MatchId = matchId,
            Channel = "Discord",
            SentAt = DateTimeOffset.UtcNow,
            Status = result.Success ? NotificationStatus.Success : NotificationStatus.Failed,
            ResponseDetail = result.Detail,
        });
        await db.SaveChangesAsync(cancellationToken);

        return result;
    }
}
