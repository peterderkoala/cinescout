using cinescout.persistence;
using Microsoft.EntityFrameworkCore;

namespace cinescout.web.Auth;

/// <summary>
/// Migrates a deployment upgrading from the old config-only credential (`Auth:PasswordHash`) onto
/// the seeded `User` row, with no operator action required. Only ever acts while the row's
/// `PasswordHash` is still null — the same first-run signal ADR 0001 already established — so a
/// later password change can never be silently reverted by a stale `.env` value on restart.
/// </summary>
public static class LegacyPasswordHashMigrator
{
    public static async Task MigrateAsync(
        CineScoutDbContext db,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(cancellationToken);
        if (user is null || user.PasswordHash is not null)
        {
            return;
        }

        var legacyHash = configuration["Auth:PasswordHash"];
        if (string.IsNullOrEmpty(legacyHash))
        {
            return;
        }

        user.PasswordHash = legacyHash;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Legacy Auth__PasswordHash migrated into the User table and is no longer read; safe to remove it from your .env.");
    }
}
