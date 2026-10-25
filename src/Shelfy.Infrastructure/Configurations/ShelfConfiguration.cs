using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shelfy.Domain;

namespace Shelfy.Infrastructure.Configurations;

public class ShelfConfiguration : IEntityTypeConfiguration<Shelf>
{
    public void Configure(EntityTypeBuilder<Shelf> builder)
    {
        builder.ToTable("Shelves");
        builder.HasKey(s => s.Id);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(s => s.Books).WithOne().HasForeignKey(sb => sb.ShelfId).OnDelete(DeleteBehavior.Cascade);
    }
}
