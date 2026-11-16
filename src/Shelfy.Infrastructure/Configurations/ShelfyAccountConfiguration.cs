using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shelfy.Domain;

namespace Shelfy.Infrastructure.Configurations;

public class ShelfyAccountConfiguration : IEntityTypeConfiguration<ShelfyAccount>
{
    public void Configure(EntityTypeBuilder<ShelfyAccount> builder)
    {
        // Identity's own mapping (AspNetUsers) comes from IdentityDbContext; this only ties the account to its domain user.
        builder.HasOne<User>().WithOne().HasForeignKey<ShelfyAccount>(a => a.Id).OnDelete(DeleteBehavior.Cascade);
    }
}
