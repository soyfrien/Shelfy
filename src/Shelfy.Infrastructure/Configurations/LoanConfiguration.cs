using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shelfy.Domain;

namespace Shelfy.Infrastructure.Configurations;

public class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    public void Configure(EntityTypeBuilder<Loan> builder)
    {
        builder.ToTable("Loans");
        builder.HasKey(l => l.Id);
        builder.HasOne(l => l.Copy).WithMany().HasForeignKey(l => l.CopyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(l => l.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(l => l.BorrowerId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Message).HasMaxLength(Lending.MaxMessageLength);
        builder.HasIndex(l => new { l.CopyId, l.Status });
        builder.HasIndex(l => l.BorrowerId);
        builder.HasIndex(l => l.OwnerId);
        builder.Ignore(l => l.IsOpen);
        builder.Ignore(l => l.IsPassAlong);
    }
}
