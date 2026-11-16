using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shelfy.Domain;

namespace Shelfy.Infrastructure.Configurations;

public class CopyMovementConfiguration : IEntityTypeConfiguration<CopyMovement>
{
    public void Configure(EntityTypeBuilder<CopyMovement> builder)
    {
        builder.ToTable("CopyMovements");
        builder.HasKey(m => m.Id);
        builder.HasOne<Copy>().WithMany().HasForeignKey(m => m.CopyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Loan>().WithMany().HasForeignKey(m => m.LoanId).OnDelete(DeleteBehavior.SetNull);
        // History keeps plain ids for users and shelves (no FKs): a deleted shelf must not erase or block the journey.
        builder.Property(m => m.Kind).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(m => new { m.CopyId, m.StartedAt });
        builder.Ignore(m => m.InTransit);
    }
}
