using System.Net;
using cinescout.web.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace cinescout.web.Tests;

public sealed class SetupTests : IClassFixture<PostgresWebFactory>
{
    private const string AuthCookiePrefix = ".AspNetCore.Cookies=";

    private readonly PostgresWebFactory _factory;

    public SetupTests(PostgresWebFactory factory) => _factory = factory;

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

        await using var db = _factory.CreateDbContext();
        var user = await db.Users.SingleAsync();
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
}
