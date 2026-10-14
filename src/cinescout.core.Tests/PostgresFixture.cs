using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace cinescout.core.Tests;

/// <summary>
/// One migrated Postgres container per test class (xUnit <c>IClassFixture</c>). Test classes call
/// <see cref="ResetAsync"/> from their own <c>InitializeAsync</c>, so every test starts from the
/// freshly-migrated state without paying for a container start.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();

    private string _resetSql = "";

    public DbContextOptions<CineScoutDbContext> Options { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        Options = new DbContextOptionsBuilder<CineScoutDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var db = new CineScoutDbContext(Options);
        await db.Database.MigrateAsync();

        // Users keeps its migration-seeded row (ADR 0001): instead of truncating it, the reset puts
        // it back to "not set up".
        var users = db.Model.FindEntityType(typeof(User))!.GetTableName();
        var tables = db.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(t => t is not null && t != users)
            .Distinct()
            .Select(t => $"\"{t}\"");
        _resetSql = $"""
            TRUNCATE {string.Join(", ", tables)} RESTART IDENTITY CASCADE;
            UPDATE "{users}" SET "PasswordHash" = NULL, "SetupCompletedAt" = NULL;
            """;
    }

    public async Task ResetAsync()
    {
        await using var db = new CineScoutDbContext(Options);
        await db.Database.ExecuteSqlRawAsync(_resetSql);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}
