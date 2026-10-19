using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace cinescout.persistence.Extensions;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        bool requireConnectionString)
    {
        var connectionString = configuration.GetConnectionString("Postgres");

        if (requireConnectionString && connectionString is null)
        {
            throw new InvalidOperationException("Connection string 'ConnectionStrings:Postgres' not found.");
        }

        services.AddDbContext<CineScoutDbContext>(options => options.UseNpgsql(connectionString ?? "Host=unused"));

        return services;
    }

    public static async Task ApplyMigrationsAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CineScoutDbContext>().Database.MigrateAsync();
    }
}
