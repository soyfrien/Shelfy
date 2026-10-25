using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shelfy.Infrastructure;

/// <summary>Lets `dotnet ef` create the context without running the API. Placeholder local connection only.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ShelfyDbContext>
{
    public ShelfyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ShelfyDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=shelfy;Username=shelfy;Password=shelfy_local_dev")
            .Options;
        return new ShelfyDbContext(options);
    }
}
