using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using cinescout.contracts;
using cinescout.core.Kinoheld;
using cinescout.model;
using cinescout.persistence;
using cinescout.web.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace cinescout.web.Tests;

public sealed partial class CinemasEndpointsTests : IClassFixture<CinemasEndpointsTestFactory>
{
    private readonly CinemasEndpointsTestFactory _factory;

    public CinemasEndpointsTests(CinemasEndpointsTestFactory factory) => _factory = factory;

    private static CinemaWriteRequest ExampleRequest(string externalCinemaId = "580") => new()
    {
        Name = "HALL OF FAME - Kino in Kamp-Lintfort",
        ExternalCinemaId = externalCinemaId,
        CrawlBaseUrl = "https://kamp-lintfort.hall-of-fame.website",
        IsActive = true,
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
    public async Task GetList_ReturnsCinemasOrderedByName_WithRoomCounts()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        int zebraId;
        int alphaId;
        await using (var db = _factory.CreateDbContext())
        {
            var zebra = new Cinema { Name = "Zebra Cinema", ExternalCinemaId = "z", CrawlBaseUrl = "https://z.invalid", IsActive = true };
            var alpha = new Cinema { Name = "Alpha Cinema", ExternalCinemaId = "a", CrawlBaseUrl = "https://a.invalid", IsActive = false };
            db.Cinemas.AddRange(zebra, alpha);
            await db.SaveChangesAsync();
            zebraId = zebra.Id;
            alphaId = alpha.Id;

            db.Rooms.Add(new Room { CinemaId = zebraId, ExternalAuditoriumId = "8255", Name = "Kino 1" });
            await db.SaveChangesAsync();
        }

        var cinemas = await client.GetFromJsonAsync<List<CinemaListItemDto>>("/api/cinemas");

        Assert.NotNull(cinemas);
        Assert.Equal(["Alpha Cinema", "Zebra Cinema"], cinemas.Select(c => c.Name));

        var alpha2 = cinemas.Single(c => c.Id == alphaId);
        Assert.Equal(0, alpha2.RoomCount);
        Assert.False(alpha2.IsActive);

        var zebra2 = cinemas.Single(c => c.Id == zebraId);
        Assert.Equal(1, zebra2.RoomCount);
    }

    [Fact]
    public async Task GetDetail_UnknownId_Returns404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/cinemas/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDetail_IncludesRoomsOrderedByName()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        int cinemaId;
        await using (var db = _factory.CreateDbContext())
        {
            var cinema = new Cinema { Name = "Kino am Rathaus", ExternalCinemaId = "580", CrawlBaseUrl = "https://kamp-lintfort.hall-of-fame.website", IsActive = true, KinoheldCinemaId = "2135" };
            db.Cinemas.Add(cinema);
            await db.SaveChangesAsync();
            cinemaId = cinema.Id;

            db.Rooms.AddRange(
                new Room { CinemaId = cinemaId, ExternalAuditoriumId = "8259", Name = "Saal 2" },
                new Room { CinemaId = cinemaId, ExternalAuditoriumId = "8255", Name = "Saal 1" });
            await db.SaveChangesAsync();
        }

        var dto = await client.GetFromJsonAsync<CinemaDetailDto>($"/api/cinemas/{cinemaId}");

        Assert.NotNull(dto);
        Assert.Equal("Kino am Rathaus", dto.Name);
        Assert.Equal("2135", dto.KinoheldCinemaId);
        Assert.Equal(["Saal 1", "Saal 2"], dto.Rooms.Select(r => r.Name));
    }

    [Fact]
    public async Task Post_CreatesCinema_AndReturns201WithLocation()
    {
        var client = await CreateWritableClientAsync();

        var response = await client.PostAsJsonAsync("/api/cinemas", ExampleRequest("post-create-1"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var dto = await response.Content.ReadFromJsonAsync<CinemaDetailDto>();
        Assert.NotNull(dto);
        Assert.Equal("HALL OF FAME - Kino in Kamp-Lintfort", dto.Name);
        Assert.Empty(dto.Rooms);
    }

    [Fact]
    public async Task Post_MissingName_ReturnsProblemDetails400()
    {
        var client = await CreateWritableClientAsync();

        var response = await client.PostAsJsonAsync("/api/cinemas", ExampleRequest("post-invalid-1") with { Name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Name is required", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_MalformedCrawlBaseUrl_ReturnsProblemDetails400()
    {
        var client = await CreateWritableClientAsync();

        var response = await client.PostAsJsonAsync("/api/cinemas", ExampleRequest("post-invalid-2") with { CrawlBaseUrl = "not-a-url" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_DuplicateExternalCinemaId_ReturnsProblemDetails409()
    {
        var client = await CreateWritableClientAsync();

        var first = await client.PostAsJsonAsync("/api/cinemas", ExampleRequest("dup-1"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/cinemas", ExampleRequest("dup-1") with { Name = "A Different Cinema" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Put_DuplicateExternalCinemaIdFromAnotherCinema_ReturnsProblemDetails409()
    {
        var client = await CreateWritableClientAsync();

        var first = await (await client.PostAsJsonAsync("/api/cinemas", ExampleRequest("dup-put-1"))).Content.ReadFromJsonAsync<CinemaDetailDto>();
        var second = await (await client.PostAsJsonAsync("/api/cinemas", ExampleRequest("dup-put-2"))).Content.ReadFromJsonAsync<CinemaDetailDto>();
        Assert.NotNull(first);
        Assert.NotNull(second);

        var response = await client.PutAsJsonAsync($"/api/cinemas/{second.Id}", ExampleRequest("dup-put-1"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithoutAntiforgeryToken_Returns400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/cinemas", ExampleRequest("post-no-csrf"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_UpdatesExistingCinema()
    {
        var client = await CreateWritableClientAsync();

        var created = await (await client.PostAsJsonAsync("/api/cinemas", ExampleRequest("put-1"))).Content.ReadFromJsonAsync<CinemaDetailDto>();
        Assert.NotNull(created);

        var response = await client.PutAsJsonAsync($"/api/cinemas/{created.Id}", ExampleRequest("put-1") with { Name = "Renamed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<CinemaDetailDto>();
        Assert.NotNull(updated);
        Assert.Equal("Renamed", updated.Name);
    }

    [Fact]
    public async Task Put_UnknownId_Returns404()
    {
        var client = await CreateWritableClientAsync();

        var response = await client.PutAsJsonAsync("/api/cinemas/999999", ExampleRequest("put-unknown"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_CinemaWithNoRooms_Returns204()
    {
        var client = await CreateWritableClientAsync();

        var created = await (await client.PostAsJsonAsync("/api/cinemas", ExampleRequest("delete-1"))).Content.ReadFromJsonAsync<CinemaDetailDto>();
        Assert.NotNull(created);

        var response = await client.DeleteAsync($"/api/cinemas/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_CinemaWithRooms_ReturnsProblemDetails409()
    {
        var client = await CreateWritableClientAsync();

        var created = await (await client.PostAsJsonAsync("/api/cinemas", ExampleRequest("delete-2"))).Content.ReadFromJsonAsync<CinemaDetailDto>();
        Assert.NotNull(created);

        await using (var db = _factory.CreateDbContext())
        {
            db.Rooms.Add(new Room { CinemaId = created.Id, ExternalAuditoriumId = "8255", Name = "Kino 1" });
            await db.SaveChangesAsync();
        }

        var response = await client.DeleteAsync($"/api/cinemas/{created.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var read = _factory.CreateDbContext();
        Assert.True(await read.Cinemas.AnyAsync(c => c.Id == created.Id));
    }

    [Fact]
    public async Task PutRoom_RenamesRoom()
    {
        var client = await CreateWritableClientAsync();

        var created = await (await client.PostAsJsonAsync("/api/cinemas", ExampleRequest("rename-1"))).Content.ReadFromJsonAsync<CinemaDetailDto>();
        Assert.NotNull(created);

        int roomId;
        await using (var db = _factory.CreateDbContext())
        {
            var room = new Room { CinemaId = created.Id, ExternalAuditoriumId = "8255", Name = "Kino 1" };
            db.Rooms.Add(room);
            await db.SaveChangesAsync();
            roomId = room.Id;
        }

        var response = await client.PutAsJsonAsync($"/api/cinemas/{created.Id}/rooms/{roomId}", new RoomRenameRequest { Name = "Renamed Room" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<RoomDto>();
        Assert.NotNull(dto);
        Assert.Equal("Renamed Room", dto.Name);
        Assert.Equal("8255", dto.ExternalAuditoriumId);
    }

    [Fact]
    public async Task ReseedRooms_NothingCrawledYet_ReturnsProblemDetails409WithoutKinoheldStatus()
    {
        var client = await CreateWritableClientAsync();

        var created = await (await client.PostAsJsonAsync("/api/cinemas", ExampleRequest("reseed-1"))).Content.ReadFromJsonAsync<CinemaDetailDto>();
        Assert.NotNull(created);

        var response = await client.PostAsync($"/api/cinemas/{created.Id}/reseed-rooms", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("kinoheldStatus", body, StringComparison.Ordinal);
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

/// <summary>
/// Separate test class, own <see cref="CinemasEndpointsTestFactory"/> instance (own Postgres
/// container, own DI container): <see cref="KinoheldCircuitBreaker"/> is a permanent one-way trip
/// with no reset, so tripping it here must not leak into <see cref="CinemasEndpointsTests"/>'
/// shared fixture.
/// </summary>
public sealed partial class CinemasEndpointsKinoheldStatusTests : IClassFixture<CinemasEndpointsTestFactory>
{
    private readonly CinemasEndpointsTestFactory _factory;

    public CinemasEndpointsKinoheldStatusTests(CinemasEndpointsTestFactory factory) => _factory = factory;

    [Fact]
    public async Task ReseedRooms_WhileBreakerTripped_ReturnsBreakerOpenKinoheldStatus()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var homeHtml = await client.GetStringAsync("/");
        var token = AntiforgeryTokenMetaTagRegex().Match(homeHtml).Groups[1].Value;
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);

        var created = await (await client.PostAsJsonAsync("/api/cinemas", new CinemaWriteRequest
        {
            Name = "HALL OF FAME - Kino in Kamp-Lintfort",
            ExternalCinemaId = "breaker-1",
            CrawlBaseUrl = "https://kamp-lintfort.hall-of-fame.website",
            IsActive = true,
        })).Content.ReadFromJsonAsync<CinemaDetailDto>();
        Assert.NotNull(created);

        var breaker = _factory.Services.GetRequiredService<KinoheldCircuitBreaker>();
        breaker.Trip("test-forced-trip");

        var response = await client.PostAsync($"/api/cinemas/{created.Id}/reseed-rooms", content: null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"kinoheldStatus\":\"breaker-open\"", body, StringComparison.Ordinal);
    }

    [GeneratedRegex("""<meta name="antiforgery-token" content="([^"]+)"\s*/>""")]
    private static partial Regex AntiforgeryTokenMetaTagRegex();
}

public sealed class CinemasEndpointsTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
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

    /// <summary>Tests share one Postgres container/schema; this clears out Cinema/Room rows so list/order assertions aren't polluted by other tests' data.</summary>
    public async Task ResetDatabaseAsync()
    {
        await using var db = CreateDbContext();
        db.Rooms.RemoveRange(db.Rooms);
        db.Cinemas.RemoveRange(db.Cinemas);
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
