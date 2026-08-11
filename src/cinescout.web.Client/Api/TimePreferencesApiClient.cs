using System.Net.Http.Json;
using cinescout.contracts;

namespace cinescout.web.Client.Api;

/// <summary>
/// The Time Preferences screen's per-screen client wrapper — same shape as <see cref="CinemasApiClient"/>
/// (#90's convention): constructor-injected with the shared <see cref="HttpClient"/>. HTTP plumbing
/// (antiforgery header, ProblemDetails parsing) lives in <see cref="ApiClientBase"/>.
/// </summary>
public sealed class TimePreferencesApiClient(HttpClient httpClient, AntiforgeryTokenStore antiforgeryTokenStore)
    : ApiClientBase(httpClient, antiforgeryTokenStore)
{
    public async Task<IReadOnlyList<TimeWindowDto>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var windows = await HttpClient.GetFromJsonAsync<IReadOnlyList<TimeWindowDto>>("api/time-preferences", cancellationToken);
        return windows ?? [];
    }

    public Task<(TimeWindowDto? Window, ApiProblem? Problem)> CreateAsync(TimeWindowWriteRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<TimeWindowDto>(HttpMethod.Post, "api/time-preferences", request, cancellationToken);

    public Task<(TimeWindowDto? Window, ApiProblem? Problem)> UpdateAsync(int id, TimeWindowWriteRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<TimeWindowDto>(HttpMethod.Put, $"api/time-preferences/{id}", request, cancellationToken);

    public async Task<ApiProblem?> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var (_, problem) = await SendAsync<object>(HttpMethod.Delete, $"api/time-preferences/{id}", body: null, cancellationToken);
        return problem;
    }
}
