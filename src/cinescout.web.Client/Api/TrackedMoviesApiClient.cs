using System.Net.Http.Json;
using cinescout.contracts;

namespace cinescout.web.Client.Api;

/// <summary>
/// The Tracked Movies screen's per-screen client wrapper (#90's convention, same shape as
/// <see cref="CinemasApiClient"/>): constructor-injected with the shared <see cref="HttpClient"/>.
/// HTTP plumbing (antiforgery header, ProblemDetails parsing) lives in <see cref="ApiClientBase"/>.
/// Track/Untrack return the full page again (#95) — a single action moves a film between both lists.
/// </summary>
public sealed class TrackedMoviesApiClient(HttpClient httpClient, AntiforgeryTokenStore antiforgeryTokenStore)
    : ApiClientBase(httpClient, antiforgeryTokenStore)
{
    public async Task<TrackedMoviesPageDto> GetAsync(CancellationToken cancellationToken = default) =>
        await HttpClient.GetFromJsonAsync<TrackedMoviesPageDto>("api/tracked-movies", cancellationToken)
            ?? throw new InvalidOperationException("GET api/tracked-movies returned an empty body.");

    public Task<(TrackedMoviesPageDto? Page, ApiProblem? Problem)> TrackAsync(int filmId, CancellationToken cancellationToken = default) =>
        SendAsync<TrackedMoviesPageDto>(HttpMethod.Post, $"api/tracked-movies/{filmId}/track", body: null, cancellationToken);

    public Task<(TrackedMoviesPageDto? Page, ApiProblem? Problem)> UntrackAsync(int filmId, CancellationToken cancellationToken = default) =>
        SendAsync<TrackedMoviesPageDto>(HttpMethod.Post, $"api/tracked-movies/{filmId}/untrack", body: null, cancellationToken);
}
