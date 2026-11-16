using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shelfy.Domain;

namespace Shelfy.Infrastructure.Configurations;

public class CopyConfiguration : IEntityTypeConfiguration<Copy>
{
    public void Configure(EntityTypeBuilder<Copy> builder)
    {
        builder.ToTable("Copies");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Code).HasMaxLength(CopyCode.Length).IsRequired();
        builder.HasIndex(c => c.Code).IsUnique();
        builder.HasOne(c => c.Book).WithMany().HasForeignKey(c => c.BookId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.CurrentHolderId).OnDelete(DeleteBehavior.Restrict);
        // Deleting a shelf (ADR-010) must not delete the physical books that were on it.
        builder.HasOne<Shelf>().WithMany().HasForeignKey(c => c.HomeShelfId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Shelf>().WithMany().HasForeignKey(c => c.CurrentShelfId).OnDelete(DeleteBehavior.SetNull);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Condition).HasMaxLength(100);
        builder.Property(c => c.Notes).HasMaxLength(1000);
        builder.Property(c => c.Version).IsConcurrencyToken();
        builder.HasIndex(c => c.BookId);
        builder.HasIndex(c => c.OwnerId);
        builder.HasIndex(c => c.CurrentShelfId);
        builder.HasIndex(c => c.HomeShelfId);
    }
}
