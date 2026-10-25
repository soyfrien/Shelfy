using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shelfy.Domain;

namespace Shelfy.Infrastructure.Configurations;

public class ShelfBookConfiguration : IEntityTypeConfiguration<ShelfBook>
{
    public void Configure(EntityTypeBuilder<ShelfBook> builder)
    {
        builder.ToTable("ShelfBooks");
        builder.HasKey(sb => new { sb.ShelfId, sb.BookId });
        builder.HasOne(sb => sb.Book).WithMany().HasForeignKey(sb => sb.BookId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(sb => sb.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(sb => sb.SalePrice).HasPrecision(10, 2);
    }
}
