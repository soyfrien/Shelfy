using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shelfy.Domain;

namespace Shelfy.Infrastructure.Configurations;

public class DetectedSpineConfiguration : IEntityTypeConfiguration<DetectedSpine>
{
    public void Configure(EntityTypeBuilder<DetectedSpine> builder)
    {
        builder.ToTable("DetectedSpines");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.RawText).HasMaxLength(1000).IsRequired();
        builder.Property(s => s.CandidateTitle).HasMaxLength(500).IsRequired();
        builder.Property(s => s.CandidateAuthor).HasMaxLength(300);
        builder.Property(s => s.CandidateIsbn).HasMaxLength(20);
        builder.Property(s => s.ResolvedIsbn).HasMaxLength(20);
        builder.Property(s => s.MatchSource).HasMaxLength(30);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(s => s.Book).WithMany().HasForeignKey(s => s.BookId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(s => s.RecognitionJobId);
    }
}
