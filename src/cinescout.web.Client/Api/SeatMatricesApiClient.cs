using System.Net.Http.Json;
using cinescout.contracts;

namespace cinescout.web.Client.Api;

/// <summary>
/// The Seat Matrices screen's per-screen client wrapper — same shape as <see cref="CinemasApiClient"/>/
/// <see cref="TimePreferencesApiClient"/> (#90's convention), deriving <see cref="ApiClientBase"/> for
/// the shared antiforgery/ProblemDetails HTTP plumbing (#93).
/// </summary>
public sealed class SeatMatricesApiClient(HttpClient httpClient, AntiforgeryTokenStore antiforgeryTokenStore)
    : ApiClientBase(httpClient, antiforgeryTokenStore)
{
    public async Task<SeatMatricesPageDto?> GetPageAsync(CancellationToken cancellationToken = default) =>
        await HttpClient.GetFromJsonAsync<SeatMatricesPageDto>("api/seat-matrices", cancellationToken);

    public Task<(SeatMatrixDto? Matrix, ApiProblem? Problem)> CreateAsync(SeatMatrixWriteRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<SeatMatrixDto>(HttpMethod.Post, "api/seat-matrices", request, cancellationToken);

    public Task<(SeatMatrixDto? Matrix, ApiProblem? Problem)> UpdateAsync(int id, SeatMatrixWriteRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<SeatMatrixDto>(HttpMethod.Put, $"api/seat-matrices/{id}", request, cancellationToken);

    public async Task<ApiProblem?> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var (_, problem) = await SendAsync<object>(HttpMethod.Delete, $"api/seat-matrices/{id}", body: null, cancellationToken);
        return problem;
    }

    public Task<(SeatMatrixDto? Matrix, ApiProblem? Problem)> ToggleEnabledAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync<SeatMatrixDto>(HttpMethod.Post, $"api/seat-matrices/{id}/toggle", body: null, cancellationToken);
}
