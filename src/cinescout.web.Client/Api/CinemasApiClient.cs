using System.Net.Http.Json;
using System.Text.Json.Serialization;
using cinescout.contracts;

namespace cinescout.web.Client.Api;

/// <summary>
/// The Cinemas screen's per-screen client wrapper (#90's convention, following
/// <see cref="PingApiClient"/>'s shape): constructor-injected with the shared <see cref="HttpClient"/>,
/// attaches the antiforgery header on every mutating call. Business-rule failures (validation,
/// delete-blocked, Kinoheld breaker/cooldown) come back as a typed <see cref="ApiProblem"/> rather
/// than an exception — Cinemas.razor renders them inline; only a genuinely unexpected status throws.
/// </summary>
public sealed class CinemasApiClient(HttpClient httpClient, AntiforgeryTokenStore antiforgeryTokenStore)
{
    public async Task<IReadOnlyList<CinemaListItemDto>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var cinemas = await httpClient.GetFromJsonAsync<IReadOnlyList<CinemaListItemDto>>("api/cinemas", cancellationToken);
        return cinemas ?? [];
    }

    public async Task<CinemaDetailDto?> GetDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/cinemas/{id}", cancellationToken);
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

    private async Task<(TResponse? Value, ApiProblem? Problem)> SendAsync<TResponse>(HttpMethod method, string uri, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (antiforgeryTokenStore.Token is { } token)
        {
            request.Headers.Add("X-CSRF-TOKEN", token);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return (default, null);
            }

            var value = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken);
            return (value, null);
        }

        return (default, await ReadProblemAsync(response, cancellationToken));
    }

    /// <summary>
    /// Some failure responses (e.g. a plain 404 from TypedResults.NotFound()) carry no body at
    /// all, not a ProblemDetails JSON object — reading them as JSON unconditionally would throw
    /// and crash the caller instead of surfacing a problem the UI can show.
    /// </summary>
    private static async Task<ApiProblem> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is null or 0)
        {
            return new ApiProblem(response.ReasonPhrase, null, null);
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<ApiProblem>(cancellationToken)
                ?? new ApiProblem(response.ReasonPhrase, null, null);
        }
        catch (System.Text.Json.JsonException)
        {
            return new ApiProblem(response.ReasonPhrase, null, null);
        }
    }
}

public sealed record ApiProblem(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("detail")] string? Detail,
    [property: JsonPropertyName("kinoheldStatus")] string? KinoheldStatus);
