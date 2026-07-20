using cinescout.persistence;
using cinescout.web.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace cinescout.web.Tests;

public class LegacyPasswordHashMigratorTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var context = new CineScoutDbContext(BuildOptions());
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    private DbContextOptions<CineScoutDbContext> BuildOptions() =>
        new DbContextOptionsBuilder<CineScoutDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

    private static IConfiguration ConfigurationWithLegacyHash(string? legacyHash) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(legacyHash is null
                ? []
                : [new KeyValuePair<string, string?>("Auth:PasswordHash", legacyHash)])
            .Build();

    [Fact]
    public async Task MigrateAsync_WithLegacyHashConfigured_CopiesItIntoTheSeededUserRow()
    {
        const string legacyHash = "legacy-hash-value";

        await using (var db = new CineScoutDbContext(BuildOptions()))
        {
            await LegacyPasswordHashMigrator.MigrateAsync(db, ConfigurationWithLegacyHash(legacyHash), NullLogger.Instance);
        }

        await using var verify = new CineScoutDbContext(BuildOptions());
        var user = await verify.Users.SingleAsync();
        Assert.Equal(legacyHash, user.PasswordHash);
    }

    [Fact]
    public async Task MigrateAsync_WithNoLegacyHashConfigured_LeavesPasswordHashNull()
    {
        await using (var db = new CineScoutDbContext(BuildOptions()))
        {
            await LegacyPasswordHashMigrator.MigrateAsync(db, ConfigurationWithLegacyHash(null), NullLogger.Instance);
        }

        await using var verify = new CineScoutDbContext(BuildOptions());
        var user = await verify.Users.SingleAsync();
        Assert.Null(user.PasswordHash);
    }

    [Fact]
    public async Task MigrateAsync_WhenPasswordHashAlreadySet_NeverOverwritesItWithALegacyValue()
    {
        const string existingHash = "already-set-hash";

        await using (var setup = new CineScoutDbContext(BuildOptions()))
        {
            var user = await setup.Users.SingleAsync();
            user.PasswordHash = existingHash;
            await setup.SaveChangesAsync();
        }

        await using (var db = new CineScoutDbContext(BuildOptions()))
        {
            await LegacyPasswordHashMigrator.MigrateAsync(db, ConfigurationWithLegacyHash("different-legacy-hash"), NullLogger.Instance);
        }

        await using var verify = new CineScoutDbContext(BuildOptions());
        var user2 = await verify.Users.SingleAsync();
        Assert.Equal(existingHash, user2.PasswordHash);
    }
}
