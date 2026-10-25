using Microsoft.EntityFrameworkCore;
using Shelfy.Domain;

namespace Shelfy.Infrastructure;

public class ShelfyDbContext(DbContextOptions<ShelfyDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Shelf> Shelves => Set<Shelf>();
    public DbSet<ShelfBook> ShelfBooks => Set<ShelfBook>();
    public DbSet<RecognitionJob> RecognitionJobs => Set<RecognitionJob>();
    public DbSet<DetectedSpine> DetectedSpines => Set<DetectedSpine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShelfyDbContext).Assembly);
    }
}
