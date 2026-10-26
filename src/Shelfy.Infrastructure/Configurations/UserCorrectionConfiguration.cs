using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shelfy.Domain;

namespace Shelfy.Infrastructure.Configurations;

public class UserCorrectionConfiguration : IEntityTypeConfiguration<UserCorrection>
{
    public void Configure(EntityTypeBuilder<UserCorrection> builder)
    {
        builder.ToTable("UserCorrections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.TextKey).HasMaxLength(500).IsRequired();
        builder.HasIndex(c => c.TextKey).IsUnique();
        builder.HasOne(c => c.Book).WithMany().HasForeignKey(c => c.BookId).OnDelete(DeleteBehavior.Cascade);
    }
}
