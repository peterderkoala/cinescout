using System.Net;
using System.Text.RegularExpressions;
using cinescout.persistence;
using cinescout.web.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace cinescout.web.Tests;

public sealed partial class ApiHardeningTests : IClassFixture<ApiHardeningTests.Factory>
{
    private const string CorrectPassword = "correct-horse-battery-staple";

    private readonly Factory _factory;

    public ApiHardeningTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task GetApiPing_WhenUnauthenticated_Returns401NotRedirect()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // /api/ping only maps POST; a GET here falls through to the Blazor host's catch-all
        // fallback route (which serves App.razor for client-side routing) rather than a 404/405 —
        // that fallback endpoint carries no [AllowAnonymous], so FallbackPolicy still applies and
        // this still proves the 401-not-redirect override for an unauthenticated /api request.
        var response = await client.GetAsync("/api/ping");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Location"));
    }

    [Fact]
    public async Task PostApiPing_WhenUnauthenticated_Returns401NotRedirect()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync("/api/ping", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Location"));
    }

    [Fact]
    public async Task PostApiPing_WhileAuthenticatedWithoutAntiforgeryToken_Returns400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // A GET to "/" runs App.razor's static-SSR pass, which sets the antiforgery cookie — proves
        // the 400 below is a missing/invalid *token*, not simply a missing cookie.
        await client.GetAsync("/");

        var response = await client.PostAsync("/api/ping", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostApiPing_WhileAuthenticatedWithValidAntiforgeryToken_Succeeds()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var homeHtml = await client.GetStringAsync("/");
        var token = ExtractAntiforgeryToken(homeHtml);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ping");
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Login_SetsAuthCookieWithSameSiteStrict()
    {
        await _factory.SetSeededUserPasswordHashAsync(CorrectPassword);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("password", CorrectPassword)]));

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
        var token = ExtractAntiforgeryToken(homeHtml);

        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenMetaTagRegex().Match(html);
        Assert.True(match.Success, "Expected an antiforgery-token <meta> tag in the response HTML.");

        return match.Groups[1].Value;
    }

    [GeneratedRegex("""<meta name="antiforgery-token" content="([^"]+)"\s*/>""")]
    private static partial Regex AntiforgeryTokenMetaTagRegex();

    public sealed class Factory : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
            .Build();

        public async Task InitializeAsync()
        {
            await _postgres.StartAsync();

            await using var context = new CineScoutDbContext(BuildOptions());
            await context.Database.MigrateAsync();
        }

        async Task IAsyncLifetime.DisposeAsync()
        {
            await _postgres.DisposeAsync();
            await DisposeAsync();
        }

        public async Task SetSeededUserPasswordHashAsync(string? password)
        {
            await using var db = new CineScoutDbContext(BuildOptions());
            var user = await db.Users.SingleAsync();
            user.PasswordHash = password is null ? null : new PasswordHasher<AppUser>().HashPassword(AppUser.Instance, password);
            user.SetupCompletedAt = password is null ? null : DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        // WebApplicationFactory's default client keeps cookies across requests on the same
        // instance (HandleCookies defaults to true), so logging in once here makes every later
        // request on the returned client an authenticated one, same as a real browser session.
        public async Task<HttpClient> CreateAuthenticatedClientAsync()
        {
            const string password = "correct-horse-battery-staple";
            await SetSeededUserPasswordHashAsync(password);

            var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await client.PostAsync(
                "/account/login",
                new FormUrlEncodedContent([new KeyValuePair<string, string>("password", password)]));

            return client;
        }

        private DbContextOptions<CineScoutDbContext> BuildOptions() =>
            new DbContextOptionsBuilder<CineScoutDbContext>()
                .UseNpgsql(_postgres.GetConnectionString())
                .Options;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
        }
    }
}
