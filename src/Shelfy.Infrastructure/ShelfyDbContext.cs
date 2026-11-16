using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Shelfy.Domain;

namespace Shelfy.Infrastructure;

/// <summary>
/// The single database context. It is also the ASP.NET Core Identity store (AspNetUsers etc., ADR-012); an account's Id equals
/// its domain User's Id.
/// </summary>
public class ShelfyDbContext(DbContextOptions<ShelfyDbContext> options)
    : IdentityDbContext<ShelfyAccount, IdentityRole<Guid>, Guid>(options)
{
    // "new": IdentityDbContext has its own Users (the accounts); in Shelfy, Users means the domain users, Accounts the logins.
    public new DbSet<User> Users => Set<User>();
    public DbSet<ShelfyAccount> Accounts => Set<ShelfyAccount>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Shelf> Shelves => Set<Shelf>();
    public DbSet<ShelfBook> ShelfBooks => Set<ShelfBook>();
    public DbSet<RecognitionJob> RecognitionJobs => Set<RecognitionJob>();
    public DbSet<DetectedSpine> DetectedSpines => Set<DetectedSpine>();
    public DbSet<UserCorrection> UserCorrections => Set<UserCorrection>();
    public DbSet<Copy> Copies => Set<Copy>();
    public DbSet<CopyMovement> CopyMovements => Set<CopyMovement>();
    public DbSet<Loan> Loans => Set<Loan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShelfyDbContext).Assembly);
    }
}
