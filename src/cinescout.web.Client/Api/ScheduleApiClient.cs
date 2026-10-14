using System.Net.Http.Json;
using cinescout.contracts;

namespace cinescout.web.Client.Api;

/// <summary>
/// The Schedule screen's per-screen client wrapper (#90's convention, same shape as
/// <see cref="TrackedMoviesApiClient"/>). GET-only — Schedule has no mutation of its own.
/// </summary>
public sealed class ScheduleApiClient(HttpClient httpClient, AntiforgeryTokenStore antiforgeryTokenStore)
    : ApiClientBase(httpClient, antiforgeryTokenStore)
{
    public async Task<ScheduleDto> GetAsync(int weekOffset, CancellationToken cancellationToken = default) =>
        await HttpClient.GetFromJsonAsync<ScheduleDto>($"api/schedule?weekOffset={weekOffset}", cancellationToken)
            ?? throw new InvalidOperationException("GET api/schedule returned an empty body.");
}
