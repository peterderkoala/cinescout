using System.Net.Http.Json;
using cinescout.contracts;

namespace cinescout.web.Client.Api;

/// <summary>
/// The Performance Detail screen's per-screen client wrapper (#90's convention, same shape as
/// <see cref="CinemasApiClient"/>): constructor-injected with the shared <see cref="HttpClient"/>.
/// HTTP plumbing (antiforgery header, ProblemDetails parsing) lives in <see cref="ApiClientBase"/>.
/// </summary>
public sealed class PerformanceDetailApiClient(HttpClient httpClient, AntiforgeryTokenStore antiforgeryTokenStore)
    : ApiClientBase(httpClient, antiforgeryTokenStore)
{
    public async Task<PerformanceDetailDto?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await HttpClient.GetAsync($"api/performances/{id}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PerformanceDetailDto>(cancellationToken);
    }

    public Task<(PerformanceDetailDto? Detail, ApiProblem? Problem)> ForceRefreshAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync<PerformanceDetailDto>(HttpMethod.Post, $"api/performances/{id}/force-refresh", body: null, cancellationToken);
}
