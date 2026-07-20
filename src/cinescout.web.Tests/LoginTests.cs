using System.Net;
using cinescout.persistence;
using cinescout.web.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace cinescout.web.Tests;

public sealed class LoginTests : IClassFixture<LoginTests.Factory>
{
    private const string CorrectPassword = "correct-horse-battery-staple";
    private const string AuthCookiePrefix = ".AspNetCore.Cookies=";

    private readonly Factory _factory;

    public LoginTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task Login_WithCorrectPassword_IssuesAuthCookie()
    {
        await _factory.SetSeededUserPasswordHashAsync(CorrectPassword);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync("/account/login", FormBody(CorrectPassword));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(AuthCookiePrefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_WithWrongPassword_IssuesNoCookieAndRedirectsBackToLogin()
    {
        await _factory.SetSeededUserPasswordHashAsync(CorrectPassword);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync("/account/login", FormBody("not-the-password"));

        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Login_WhenUserHasNoPasswordHashSet_AlwaysFails()
    {
        await _factory.SetSeededUserPasswordHashAsync(null);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync("/account/login", FormBody(CorrectPassword));

        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task UnauthenticatedRequest_ToProtectedPage_RedirectsToLogin()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.ToString());
    }

    private static FormUrlEncodedContent FormBody(string password) =>
        new([new KeyValuePair<string, string>("password", password)]);

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

        // The seeded User row starts with PasswordHash == null (ADR 0001) — tests set it directly
        // against the same real Postgres the app uses, rather than going through a config-based
        // credential, matching the login flow's actual source of truth after this change.
        public async Task SetSeededUserPasswordHashAsync(string? password)
        {
            await using var db = new CineScoutDbContext(BuildOptions());
            var user = await db.Users.SingleAsync();
            user.PasswordHash = password is null ? null : new PasswordHasher<AppUser>().HashPassword(AppUser.Instance, password);
            await db.SaveChangesAsync();
        }

        private DbContextOptions<CineScoutDbContext> BuildOptions() =>
            new DbContextOptionsBuilder<CineScoutDbContext>()
                .UseNpgsql(_postgres.GetConnectionString())
                .Options;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            // UseSetting (not ConfigureAppConfiguration) — Program.cs reads
            // builder.Configuration.GetConnectionString("Postgres") into a local variable before
            // Build() runs, and ConfigureAppConfiguration's provider isn't spliced in early enough
            // for that read to see it. UseSetting applies synchronously, early enough to be visible.
            builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
        }
    }
}
