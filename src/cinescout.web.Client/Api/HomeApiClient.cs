using System.Net.Http.Json;
using cinescout.contracts;

namespace cinescout.web.Client.Api;

/// <summary>
/// The Home screen's per-screen client wrapper — same shape as <see cref="CinemasApiClient"/>
/// (#90's convention): constructor-injected with the shared <see cref="HttpClient"/>. HTTP plumbing
/// (antiforgery header, ProblemDetails parsing) lives in <see cref="ApiClientBase"/>. Read-only —
/// Home has no mutating actions of its own (#97).
/// </summary>
public sealed class HomeApiClient(HttpClient httpClient, AntiforgeryTokenStore antiforgeryTokenStore)
    : ApiClientBase(httpClient, antiforgeryTokenStore)
{
    public async Task<HomePageDto?> GetAsync(CancellationToken cancellationToken = default) =>
        await HttpClient.GetFromJsonAsync<HomePageDto>("api/home", cancellationToken);
}
