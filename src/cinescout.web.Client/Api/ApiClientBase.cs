using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace cinescout.web.Client.Api;

/// <summary>
/// Shared HTTP plumbing every per-screen API client wrapper needs (#90's convention): attaches the
/// antiforgery header on mutating calls, and surfaces business-rule failures (validation,
/// delete-blocked, etc.) as a typed <see cref="ApiProblem"/> rather than an exception, so the page
/// can render them inline — only a genuinely unexpected status throws. Extracted after
/// <c>CinemasApiClient</c> and <c>TimePreferencesApiClient</c> reimplemented this verbatim (#93's
/// review) — every remaining per-screen client (#94–#98) should derive from this instead of copying
/// <c>SendAsync</c>/<c>ReadProblemAsync</c> again.
/// </summary>
public abstract class ApiClientBase(HttpClient httpClient, AntiforgeryTokenStore antiforgeryTokenStore)
{
    /// <summary>
    /// Exposed for derived classes' own GET calls (SendAsync below covers mutations) — referencing
    /// this instead of the primary constructor parameter directly avoids each derived class capturing
    /// its own separate copy of the parameter (CS9107).
    /// </summary>
    protected HttpClient HttpClient { get; } = httpClient;

    protected async Task<(TResponse? Value, ApiProblem? Problem)> SendAsync<TResponse>(HttpMethod method, string uri, object? body, CancellationToken cancellationToken)
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

        using var response = await HttpClient.SendAsync(request, cancellationToken);

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
        catch (JsonException)
        {
            return new ApiProblem(response.ReasonPhrase, null, null);
        }
    }
}

public sealed record ApiProblem(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("detail")] string? Detail,
    [property: JsonPropertyName("kinoheldStatus")] string? KinoheldStatus);
