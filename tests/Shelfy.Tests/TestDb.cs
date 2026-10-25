using Microsoft.EntityFrameworkCore;
using Shelfy.Infrastructure;

namespace Shelfy.Tests;

internal static class TestDb
{
    /// <summary>Fresh in-memory context per test.</summary>
    public static ShelfyDbContext Create() =>
        new(new DbContextOptionsBuilder<ShelfyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
