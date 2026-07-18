using System.Net;
using cinescout.web.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

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
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync("/account/login", FormBody("not-the-password"));

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

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var hasher = new PasswordHasher<AppUser>();
                config.AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>(
                        "Auth:PasswordHash",
                        hasher.HashPassword(AppUser.Instance, CorrectPassword)),
                ]);
            });
        }
    }
}
