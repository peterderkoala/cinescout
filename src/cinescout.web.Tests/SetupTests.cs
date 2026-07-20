using System.Net;
using cinescout.persistence;
using cinescout.web.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace cinescout.web.Tests;

public sealed class SetupTests : IClassFixture<SetupTests.Factory>
{
    private const string AuthCookiePrefix = ".AspNetCore.Cookies=";

    private readonly Factory _factory;

    public SetupTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task GetLogin_WhenUnset_RedirectsToSetup()
    {
        await _factory.SetSeededUserPasswordHashAsync(null);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/setup", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task PostAccountLogin_WhenUnset_RedirectsToSetup()
    {
        await _factory.SetSeededUserPasswordHashAsync(null);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("password", "whatever")]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/setup", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Setup_WithWrongToken_IssuesNoCookieAndRedirectsBackToSetup()
    {
        await _factory.SetSeededUserPasswordHashAsync(null);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync("/account/setup", FormBody("not-the-token", "password123", "password123"));

        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/setup", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Setup_WithMismatchedPasswords_IssuesNoCookieAndRedirectsBackToSetup()
    {
        await _factory.SetSeededUserPasswordHashAsync(null);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = _factory.Services.GetRequiredService<FirstRunTokenStore>().Token;

        var response = await client.PostAsync("/account/setup", FormBody(token, "password123", "different"));

        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/setup", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Setup_WithCorrectTokenAndMatchingPasswords_SetsPasswordHashAndSignsIn()
    {
        await _factory.SetSeededUserPasswordHashAsync(null);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = _factory.Services.GetRequiredService<FirstRunTokenStore>().Token;

        var response = await client.PostAsync("/account/setup", FormBody(token, "correct-horse-battery-staple", "correct-horse-battery-staple"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.ToString());
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(AuthCookiePrefix, StringComparison.Ordinal));

        var user = await _factory.GetSeededUserAsync();
        Assert.NotNull(user.PasswordHash);
        Assert.NotNull(user.SetupCompletedAt);
    }

    [Fact]
    public async Task GetSetup_OnceAlreadySetUp_RedirectsToLogin()
    {
        await _factory.SetSeededUserPasswordHashAsync("some-password");
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/setup");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task PostAccountSetup_OnceAlreadySetUp_RedirectsToLoginInsteadOfReaccepting()
    {
        await _factory.SetSeededUserPasswordHashAsync("some-password");
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = _factory.Services.GetRequiredService<FirstRunTokenStore>().Token;

        var response = await client.PostAsync("/account/setup", FormBody(token, "new-password", "new-password"));

        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.ToString());
    }

    private static FormUrlEncodedContent FormBody(string token, string password, string confirmPassword) =>
        new([
            new KeyValuePair<string, string>("token", token),
            new KeyValuePair<string, string>("password", password),
            new KeyValuePair<string, string>("confirmPassword", confirmPassword),
        ]);

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

        public async Task<cinescout.model.User> GetSeededUserAsync()
        {
            await using var db = new CineScoutDbContext(BuildOptions());
            return await db.Users.SingleAsync();
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
