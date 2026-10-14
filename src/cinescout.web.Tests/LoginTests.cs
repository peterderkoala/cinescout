using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace cinescout.web.Tests;

public sealed class LoginTests : IClassFixture<PostgresWebFactory>
{
    private const string AuthCookiePrefix = ".AspNetCore.Cookies=";

    private readonly PostgresWebFactory _factory;

    public LoginTests(PostgresWebFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_WithCorrectPassword_IssuesAuthCookie()
    {
        await _factory.SetSeededUserPasswordHashAsync(PostgresWebFactory.CorrectPassword);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync("/account/login", FormBody(PostgresWebFactory.CorrectPassword));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(AuthCookiePrefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_WithWrongPassword_IssuesNoCookieAndRedirectsBackToLogin()
    {
        await _factory.SetSeededUserPasswordHashAsync(PostgresWebFactory.CorrectPassword);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync("/account/login", FormBody("not-the-password"));

        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Login_WhenUserHasNoPasswordHashSet_RedirectsToSetupInsteadOfAttemptingVerification()
    {
        await _factory.SetSeededUserPasswordHashAsync(null);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync("/account/login", FormBody(PostgresWebFactory.CorrectPassword));

        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/setup", response.Headers.Location!.ToString());
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
}
