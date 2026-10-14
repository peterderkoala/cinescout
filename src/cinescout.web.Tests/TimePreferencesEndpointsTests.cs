using System.Net;
using System.Net.Http.Json;
using cinescout.contracts;
using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;

namespace cinescout.web.Tests;

public sealed class TimePreferencesEndpointsTests : IClassFixture<PostgresWebFactory>
{
    private readonly PostgresWebFactory _factory;

    public TimePreferencesEndpointsTests(PostgresWebFactory factory) => _factory = factory;

    private static TimeWindowWriteRequest ExampleRequest() => new()
    {
        DayCodes = ["Fr", "Sa"],
        StartTime = new TimeOnly(19, 0),
        EndTime = new TimeOnly(22, 30),
    };

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
        var client = await _factory.CreateWritableClientAsync();

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
        var client = await _factory.CreateWritableClientAsync();

        var response = await client.PostAsJsonAsync("/api/time-preferences", ExampleRequest() with { DayCodes = [] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Select at least one day.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_UnrecognizedDayCode_ReturnsProblemDetails400()
    {
        var client = await _factory.CreateWritableClientAsync();

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
        var client = await _factory.CreateWritableClientAsync();

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
        var client = await _factory.CreateWritableClientAsync();

        var response = await client.PutAsJsonAsync("/api/time-preferences/999999", ExampleRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_InvalidRequest_ReturnsProblemDetails400_AndLeavesWindowUnchanged()
    {
        var client = await _factory.CreateWritableClientAsync();

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
        var client = await _factory.CreateWritableClientAsync();

        var created = await (await client.PostAsJsonAsync("/api/time-preferences", ExampleRequest())).Content.ReadFromJsonAsync<TimeWindowDto>();
        Assert.NotNull(created);

        var first = await client.DeleteAsync($"/api/time-preferences/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await client.DeleteAsync($"/api/time-preferences/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        await using var read = _factory.CreateDbContext();
        Assert.False(await read.FavoriteTimeWindows.AnyAsync(w => w.Id == created.Id));
    }
}
