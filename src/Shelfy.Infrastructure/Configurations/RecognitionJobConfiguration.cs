using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shelfy.Domain;

namespace Shelfy.Infrastructure.Configurations;

public class RecognitionJobConfiguration : IEntityTypeConfiguration<RecognitionJob>
{
    public void Configure(EntityTypeBuilder<RecognitionJob> builder)
    {
        builder.ToTable("RecognitionJobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.PhotoKey).HasMaxLength(300).IsRequired();
        builder.Property(j => j.ContentType).HasMaxLength(50).IsRequired();
        builder.Property(j => j.PhotoSha256).HasMaxLength(64).IsRequired();
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.Error).HasMaxLength(2000);

        builder.HasOne<Shelf>().WithMany().HasForeignKey(j => j.ShelfId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(j => j.Spines).WithOne().HasForeignKey(s => s.RecognitionJobId).OnDelete(DeleteBehavior.Cascade);

        // Worker polling: "next claimable job".
        builder.HasIndex(j => new { j.Status, j.NextAttemptAt });
        // Upload idempotency: find an existing job for the same photo on the same shelf.
        builder.HasIndex(j => new { j.ShelfId, j.PhotoSha256 });
    }
}
