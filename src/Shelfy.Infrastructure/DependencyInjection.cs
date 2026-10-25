using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shelfy.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "ShelfyDb";

    public static IServiceCollection AddShelfyInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        services.AddDbContext<ShelfyDbContext>(options => options.UseNpgsql(connectionString));
        return services;
    }
}
