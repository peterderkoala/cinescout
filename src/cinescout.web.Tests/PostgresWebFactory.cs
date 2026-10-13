using cinescout.model;
using cinescout.persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace cinescout.web.Tests;

/// <summary>
/// Base for every web test factory that needs Postgres. The whole test assembly shares one
/// container; each factory (one per test class, via <c>IClassFixture</c>) gets its own database on
/// it, so classes still run in parallel without seeing each other's rows. Testcontainers' reaper
/// removes the container when the test process exits.
/// </summary>
public abstract class PostgresWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly PostgreSqlContainer Container = new PostgreSqlBuilder("postgres:18").Build();

    private static readonly Lazy<Task> ContainerStarted = new(() => Container.StartAsync());

    private string _connectionString = "";

    private string _resetSql = "";

    public async Task InitializeAsync()
    {
        await ContainerStarted.Value;
        _connectionString = new NpgsqlConnectionStringBuilder(Container.GetConnectionString())
        {
            Database = $"test_{Guid.NewGuid():N}",
        }.ConnectionString;

        // MigrateAsync creates the database first.
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        // Users keeps its migration-seeded row (ADR 0001): instead of truncating it, the reset puts
        // it back to "not set up".
        var users = db.Model.FindEntityType(typeof(User))!.GetTableName();
        var tables = db.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(t => t is not null && t != users)
            .Distinct()
            .Select(t => $"\"{t}\"");
        // No RESTART IDENTITY: the app host outlives a reset, and in-memory singletons keyed by id
        // (KinoheldFetchCooldownTracker) would see a reused id as a recent force-refresh.
        _resetSql = $"""
            TRUNCATE {string.Join(", ", tables)} CASCADE;
            UPDATE "{users}" SET "PasswordHash" = NULL, "SetupCompletedAt" = NULL;
            """;
    }

    async Task IAsyncLifetime.DisposeAsync() => await DisposeAsync();

    public CineScoutDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<CineScoutDbContext>().UseNpgsql(_connectionString).Options);

    /// <summary>Back to the freshly-migrated state: every table emptied, the seeded user not set up.</summary>
    public async Task ResetDatabaseAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync(_resetSql);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // UseSetting, not ConfigureAppConfiguration: Program.cs reads the connection string before
        // Build(), and only UseSetting applies early enough for that read to see it.
        builder.UseSetting("ConnectionStrings:Postgres", _connectionString);
    }
}
