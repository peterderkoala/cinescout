using System.Net.Http.Json;
using cinescout.contracts;

namespace cinescout.web.Client.Api;

/// <summary>The Notifications screen's per-screen client wrapper (#90's convention); plumbing lives in <see cref="ApiClientBase"/>.</summary>
public sealed class NotificationChannelsApiClient(HttpClient httpClient, AntiforgeryTokenStore antiforgeryTokenStore)
    : ApiClientBase(httpClient, antiforgeryTokenStore)
{
    public async Task<IReadOnlyList<NotificationChannelDto>> GetListAsync(CancellationToken cancellationToken = default) =>
        await HttpClient.GetFromJsonAsync<IReadOnlyList<NotificationChannelDto>>("api/notification-channels", cancellationToken) ?? [];

    public Task<(NotificationChannelDto? Channel, ApiProblem? Problem)> CreateAsync(NotificationChannelWriteRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<NotificationChannelDto>(HttpMethod.Post, "api/notification-channels", request, cancellationToken);

    public Task<(NotificationChannelDto? Channel, ApiProblem? Problem)> UpdateAsync(int id, NotificationChannelWriteRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<NotificationChannelDto>(HttpMethod.Put, $"api/notification-channels/{id}", request, cancellationToken);

    public async Task<ApiProblem?> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var (_, problem) = await SendAsync<object>(HttpMethod.Delete, $"api/notification-channels/{id}", body: null, cancellationToken);
        return problem;
    }

    /// <param name="savedChannelId">Supplies the stored webhook URL when the form's URL field was left empty.</param>
    public Task<(NotificationTestResultDto? Result, ApiProblem? Problem)> TestAsync(int? savedChannelId, NotificationChannelWriteRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<NotificationTestResultDto>(HttpMethod.Post,
            savedChannelId is { } id ? $"api/notification-channels/test?channelId={id}" : "api/notification-channels/test", request, cancellationToken);
}
