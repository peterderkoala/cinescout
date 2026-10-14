using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace cinescout.web.Tests;

public sealed class ApiHardeningTests : IClassFixture<PostgresWebFactory>
{
    private readonly PostgresWebFactory _factory;

    public ApiHardeningTests(PostgresWebFactory factory) => _factory = factory;

    // A POST endpoint under the antiforgery-filtered /api group. No Film is seeded, so a request
    // that gets past auth and the antiforgery filter reaches the handler's 404 ProblemDetails.
    private const string TrackUrl = "/api/tracked-movies/999/track";

    [Fact]
    public async Task GetApi_WhenUnauthenticated_Returns401NotRedirect()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/tracked-movies");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Location"));
    }

    [Fact]
    public async Task PostApi_WhenUnauthenticated_Returns401NotRedirect()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync(TrackUrl, content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Location"));
    }

    [Fact]
    public async Task PostApi_WhileAuthenticatedWithoutAntiforgeryToken_Returns400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // A GET to "/" runs App.razor's static-SSR pass, which sets the antiforgery cookie — proves
        // the 400 below is a missing/invalid *token*, not simply a missing cookie.
        await client.GetAsync("/");

        var response = await client.PostAsync(TrackUrl, content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostApi_WhileAuthenticatedWithValidAntiforgeryToken_ReachesTheHandler()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var homeHtml = await client.GetStringAsync("/");
        var token = PostgresWebFactory.ExtractAntiforgeryToken(homeHtml);

        using var request = new HttpRequestMessage(HttpMethod.Post, TrackUrl);
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Login_SetsAuthCookieWithSameSiteStrict()
    {
        await _factory.SetSeededUserPasswordHashAsync(PostgresWebFactory.CorrectPassword);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("password", PostgresWebFactory.CorrectPassword)]));

        var setCookieHeader = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal));
        Assert.Contains("samesite=strict", setCookieHeader, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HomeHtml_ContainsAReadableAntiforgeryToken()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var homeHtml = await client.GetStringAsync("/");
        var token = PostgresWebFactory.ExtractAntiforgeryToken(homeHtml);

        Assert.False(string.IsNullOrWhiteSpace(token));
    }
}
