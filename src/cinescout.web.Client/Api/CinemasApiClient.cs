using System.Net.Http.Json;
using cinescout.contracts;

namespace cinescout.web.Client.Api;

/// <summary>
/// The Cinemas screen's per-screen client wrapper (#90's convention, following
/// <see cref="PingApiClient"/>'s shape): constructor-injected with the shared <see cref="HttpClient"/>.
/// HTTP plumbing (antiforgery header, ProblemDetails parsing) lives in <see cref="ApiClientBase"/>.
/// </summary>
public sealed class CinemasApiClient(HttpClient httpClient, AntiforgeryTokenStore antiforgeryTokenStore)
    : ApiClientBase(httpClient, antiforgeryTokenStore)
{
    public async Task<IReadOnlyList<CinemaListItemDto>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var cinemas = await HttpClient.GetFromJsonAsync<IReadOnlyList<CinemaListItemDto>>("api/cinemas", cancellationToken);
        return cinemas ?? [];
    }

    public async Task<CinemaDetailDto?> GetDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await HttpClient.GetAsync($"api/cinemas/{id}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CinemaDetailDto>(cancellationToken);
    }

    public Task<(CinemaDetailDto? Cinema, ApiProblem? Problem)> CreateAsync(CinemaWriteRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<CinemaDetailDto>(HttpMethod.Post, "api/cinemas", request, cancellationToken);

    public Task<(CinemaDetailDto? Cinema, ApiProblem? Problem)> UpdateAsync(int id, CinemaWriteRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<CinemaDetailDto>(HttpMethod.Put, $"api/cinemas/{id}", request, cancellationToken);

    public async Task<ApiProblem?> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var (_, problem) = await SendAsync<object>(HttpMethod.Delete, $"api/cinemas/{id}", body: null, cancellationToken);
        return problem;
    }

    public Task<(CinemaDetailDto? Cinema, ApiProblem? Problem)> ReseedRoomsAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync<CinemaDetailDto>(HttpMethod.Post, $"api/cinemas/{id}/reseed-rooms", body: null, cancellationToken);

    public Task<(RoomDto? Room, ApiProblem? Problem)> RenameRoomAsync(int cinemaId, int roomId, string name, CancellationToken cancellationToken = default) =>
        SendAsync<RoomDto>(HttpMethod.Put, $"api/cinemas/{cinemaId}/rooms/{roomId}", new RoomRenameRequest { Name = name }, cancellationToken);
}
