using cinescout.contracts;
using cinescout.core.Discord;
using cinescout.persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace cinescout.web.Endpoints;

/// <summary>
/// The Notifications screen's API surface (#142): the list read queries CineScoutDbContext directly,
/// writes and "Send test" go through NotificationChannelService — matching every other screen's
/// read/write split (#83). The webhook URL is validated against the Discord allowlist here and only
/// ever leaves the server masked.
/// </summary>
public static class NotificationChannelsEndpointsExtensions
{
    public static RouteGroupBuilder MapNotificationChannelsEndpoints(this RouteGroupBuilder apiGroup)
    {
        var group = apiGroup.MapGroup("/notification-channels");

        group.MapGet("/", GetListAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id:int}", UpdateAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
        group.MapPost("/test", TestAsync);

        return apiGroup;
    }

    private static async Task<Ok<IReadOnlyList<NotificationChannelDto>>> GetListAsync(CineScoutDbContext db, CancellationToken cancellationToken) =>
        TypedResults.Ok(await QueryDtosAsync(db, id: null, cancellationToken));

    private static async Task<Results<Created<NotificationChannelDto>, ProblemHttpResult>> CreateAsync(
        NotificationChannelWriteRequest request, NotificationChannelService service, CineScoutDbContext db, CancellationToken cancellationToken)
    {
        if (NotificationChannelValidation.Validate(request, webhookUrlRequired: true) is { } error)
        {
            return InvalidChannel(error);
        }

        var id = await service.CreateAsync(ToInput(request), cancellationToken);
        return TypedResults.Created($"/api/notification-channels/{id}", (await QueryDtosAsync(db, id, cancellationToken)).Single());
    }

    private static async Task<Results<Ok<NotificationChannelDto>, NotFound, ProblemHttpResult>> UpdateAsync(
        int id, NotificationChannelWriteRequest request, NotificationChannelService service, CineScoutDbContext db, CancellationToken cancellationToken)
    {
        if (NotificationChannelValidation.Validate(request, webhookUrlRequired: false) is { } error)
        {
            return InvalidChannel(error);
        }

        if (!await service.UpdateAsync(id, ToInput(request), cancellationToken))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok((await QueryDtosAsync(db, id, cancellationToken)).Single());
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(int id, NotificationChannelService service, CancellationToken cancellationToken) =>
        await service.DeleteAsync(id, cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound();

    private static async Task<Results<Ok<NotificationTestResultDto>, NotFound, ProblemHttpResult>> TestAsync(
        int? channelId, NotificationChannelWriteRequest request, NotificationChannelService service, CancellationToken cancellationToken)
    {
        if (NotificationChannelValidation.Validate(request, webhookUrlRequired: channelId is null) is { } error)
        {
            return InvalidChannel(error);
        }

        var result = await service.SendTestAsync(channelId, ToInput(request), cancellationToken);
        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new NotificationTestResultDto { Success = result.Success, Detail = result.Detail, HttpStatus = result.HttpStatus });
    }

    private static ProblemHttpResult InvalidChannel(string error) =>
        TypedResults.Problem(title: "Invalid notification channel.", detail: error, statusCode: StatusCodes.Status400BadRequest);

    private static NotificationChannelInput ToInput(NotificationChannelWriteRequest request) => new(
        request.Name,
        string.IsNullOrWhiteSpace(request.WebhookUrl) ? null : NotificationChannelValidation.NormalizeWebhookUrl(request.WebhookUrl),
        request.DisplayName,
        request.AvatarUrl,
        request.Prefix,
        request.Suppress,
        request.IsEnabled,
        request.Types);

    private static async Task<IReadOnlyList<NotificationChannelDto>> QueryDtosAsync(CineScoutDbContext db, int? id, CancellationToken cancellationToken)
    {
        var rows = await db.NotificationChannels
            .Where(c => id == null || c.Id == id)
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Select(c => new
            {
                Channel = c,
                LastDelivery = db.NotificationLogs
                    .Where(l => l.NotificationChannelId == c.Id)
                    .OrderByDescending(l => l.SentAt)
                    .Select(l => new { l.Status, l.SentAt })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new NotificationChannelDto
        {
            Id = r.Channel.Id,
            Name = r.Channel.Name,
            WebhookUrlMasked = Mask(r.Channel.WebhookUrl),
            DisplayName = r.Channel.DisplayName,
            AvatarUrl = r.Channel.AvatarUrl,
            Prefix = r.Channel.Prefix,
            Suppress = r.Channel.Suppress,
            IsEnabled = r.Channel.IsEnabled,
            Types = r.Channel.Types,
            LastError = r.Channel.LastError,
            LastDeliveryStatus = r.LastDelivery?.Status,
            LastDeliveryAt = r.LastDelivery?.SentAt,
        }).ToList();
    }

    /// <summary>Enough of the webhook id to tell channels apart; never the token.</summary>
    private static string Mask(string webhookUrl)
    {
        var segments = new Uri(webhookUrl).Segments;
        var webhookId = segments.Length >= 2 ? segments[^2].TrimEnd('/') : "";
        return $"…/webhooks/{webhookId[..Math.Min(4, webhookId.Length)]}…/••••";
    }
}
