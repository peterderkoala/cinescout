using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace cinescout.persistence;

/// <summary>
/// Used only by `dotnet ef` design-time tooling (migrations) — the connection string here
/// is never actually opened for `migrations add`, it just tells EF Core which provider to
/// generate SQL for. The real connection string is supplied at runtime by cinescout.web.
/// </summary>
public class CineScoutDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CineScoutDbContext>
{
    public CineScoutDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CineScoutDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=cinescout;Username=cinescout;Password=design-time-only");
        return new CineScoutDbContext(optionsBuilder.Options);
    }
}
