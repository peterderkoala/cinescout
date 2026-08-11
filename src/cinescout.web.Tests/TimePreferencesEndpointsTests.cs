using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using cinescout.contracts;
using cinescout.model;
using cinescout.persistence;
using cinescout.web.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace cinescout.web.Tests;

public sealed partial class TimePreferencesEndpointsTests : IClassFixture<TimePreferencesEndpointsTestFactory>
{
    private readonly TimePreferencesEndpointsTestFactory _factory;

    public TimePreferencesEndpointsTests(TimePreferencesEndpointsTestFactory factory) => _factory = factory;

    private static TimeWindowWriteRequest ExampleRequest() => new()
    {
        DayCodes = ["Fr", "Sa"],
        StartTime = new TimeOnly(19, 0),
        EndTime = new TimeOnly(22, 30),
    };

    private async Task<HttpClient> CreateWritableClientAsync()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var homeHtml = await client.GetStringAsync("/");
        var token = ExtractAntiforgeryToken(homeHtml);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);

        return client;
    }

    [Fact]
    public async Task GetList_ReturnsWindowsOrderedByStartTime_WithDayCodes()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        await using (var db = _factory.CreateDbContext())
        {
            db.FavoriteTimeWindows.AddRange(
                new FavoriteTimeWindow { DaysOfWeek = DaysOfWeekFlags.Sunday, StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(23, 0) },
                new FavoriteTimeWindow { DaysOfWeek = DaysOfWeekFlags.Monday | DaysOfWeekFlags.Wednesday, StartTime = new TimeOnly(18, 0), EndTime = new TimeOnly(21, 0) });
            await db.SaveChangesAsync();
        }

        var windows = await client.GetFromJsonAsync<List<TimeWindowDto>>("/api/time-preferences");

        Assert.NotNull(windows);
        Assert.Equal([new TimeOnly(18, 0), new TimeOnly(20, 0)], windows.Select(w => w.StartTime));

        var monWed = windows.Single(w => w.StartTime == new TimeOnly(18, 0));
        Assert.Equal(["Mo", "We"], monWed.DayCodes);
    }

    [Fact]
    public async Task Post_CreatesTimeWindow_AndReturns201WithLocation()
    {
        var client = await CreateWritableClientAsync();

        var response = await client.PostAsJsonAsync("/api/time-preferences", ExampleRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var dto = await response.Content.ReadFromJsonAsync<TimeWindowDto>();
        Assert.NotNull(dto);
        Assert.Equal(["Fr", "Sa"], dto.DayCodes);
        Assert.Equal(new TimeOnly(19, 0), dto.StartTime);
        Assert.Equal(new TimeOnly(22, 30), dto.EndTime);
    }

    [Fact]
    public async Task Post_NoDaysSelected_ReturnsProblemDetails400()
    {
        var client = await CreateWritableClientAsync();

        var response = await client.PostAsJsonAsync("/api/time-preferences", ExampleRequest() with { DayCodes = [] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Select at least one day.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_EndTimeNotAfterStartTime_ReturnsProblemDetails400()
    {
        var client = await CreateWritableClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/time-preferences", ExampleRequest() with { StartTime = new TimeOnly(20, 0), EndTime = new TimeOnly(20, 0) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("End time must be later than start time.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_UnrecognizedDayCode_ReturnsProblemDetails400()
    {
        var client = await CreateWritableClientAsync();

        var response = await client.PostAsJsonAsync("/api/time-preferences", ExampleRequest() with { DayCodes = ["Xx"] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithoutAntiforgeryToken_Returns400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/time-preferences", ExampleRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_UpdatesExistingWindow()
    {
        var client = await CreateWritableClientAsync();

        var created = await (await client.PostAsJsonAsync("/api/time-preferences", ExampleRequest())).Content.ReadFromJsonAsync<TimeWindowDto>();
        Assert.NotNull(created);

        var response = await client.PutAsJsonAsync(
            $"/api/time-preferences/{created.Id}", ExampleRequest() with { DayCodes = ["Mo"], StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(12, 0) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<TimeWindowDto>();
        Assert.NotNull(updated);
        Assert.Equal(["Mo"], updated.DayCodes);
        Assert.Equal(new TimeOnly(10, 0), updated.StartTime);
        Assert.Equal(new TimeOnly(12, 0), updated.EndTime);
    }

    [Fact]
    public async Task Put_UnknownId_Returns404()
    {
        var client = await CreateWritableClientAsync();

        var response = await client.PutAsJsonAsync("/api/time-preferences/999999", ExampleRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_InvalidRequest_ReturnsProblemDetails400_AndLeavesWindowUnchanged()
    {
        var client = await CreateWritableClientAsync();

        var created = await (await client.PostAsJsonAsync("/api/time-preferences", ExampleRequest())).Content.ReadFromJsonAsync<TimeWindowDto>();
        Assert.NotNull(created);

        var response = await client.PutAsJsonAsync($"/api/time-preferences/{created.Id}", ExampleRequest() with { DayCodes = [] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var read = _factory.CreateDbContext();
        var window = await read.FavoriteTimeWindows.SingleAsync(w => w.Id == created.Id);
        Assert.Equal(DaysOfWeekFlags.Friday | DaysOfWeekFlags.Saturday, window.DaysOfWeek);
    }

    [Fact]
    public async Task Delete_RemovesWindow_AndIsIdempotent()
    {
        var client = await CreateWritableClientAsync();

        var created = await (await client.PostAsJsonAsync("/api/time-preferences", ExampleRequest())).Content.ReadFromJsonAsync<TimeWindowDto>();
        Assert.NotNull(created);

        var first = await client.DeleteAsync($"/api/time-preferences/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await client.DeleteAsync($"/api/time-preferences/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        await using var read = _factory.CreateDbContext();
        Assert.False(await read.FavoriteTimeWindows.AnyAsync(w => w.Id == created.Id));
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenMetaTagRegex().Match(html);
        Assert.True(match.Success, "Expected an antiforgery-token <meta> tag in the response HTML.");

        return match.Groups[1].Value;
    }

    [GeneratedRegex("""<meta name="antiforgery-token" content="([^"]+)"\s*/>""")]
    private static partial Regex AntiforgeryTokenMetaTagRegex();
}

public sealed class TimePreferencesEndpointsTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string CorrectPassword = "correct-horse-battery-staple";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await DisposeAsync();
    }

    public CineScoutDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<CineScoutDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    /// <summary>Tests share one Postgres container/schema; this clears out FavoriteTimeWindow rows so list/order assertions aren't polluted by other tests' data.</summary>
    public async Task ResetDatabaseAsync()
    {
        await using var db = CreateDbContext();
        db.FavoriteTimeWindows.RemoveRange(db.FavoriteTimeWindows);
        await db.SaveChangesAsync();
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        await using (var db = CreateDbContext())
        {
            var user = await db.Users.SingleAsync();
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(AppUser.Instance, CorrectPassword);
            user.SetupCompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("password", CorrectPassword)]));

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
    }
}
