namespace cinescout.web.Client.Api;

/// <summary>
/// The worked example #90 asks for: a lightweight per-screen client wrapper, constructor-injected
/// with the one shared <see cref="HttpClient"/> registered in <c>Program.cs</c> (never its own
/// <c>AddHttpClient&lt;T&gt;()</c>), attaching the antiforgery header on the mutating call. Real
/// screen tickets (#92–#98) add their own wrapper following this exact shape against the group's
/// real endpoints instead of <c>/api/ping</c>.
/// </summary>
public sealed class PingApiClient(HttpClient httpClient, AntiforgeryTokenStore antiforgeryTokenStore)
{
    public async Task PingAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/ping");

        if (antiforgeryTokenStore.Token is { } token)
        {
            request.Headers.Add("X-CSRF-TOKEN", token);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
