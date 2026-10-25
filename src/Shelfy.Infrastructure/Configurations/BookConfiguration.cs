using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shelfy.Domain;

namespace Shelfy.Infrastructure.Configurations;

public class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.ToTable("Books");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Isbn).HasMaxLength(20);
        builder.HasIndex(b => b.Isbn).IsUnique(); // a Book exists once; NULL ISBNs are allowed multiple times
        builder.Property(b => b.Title).HasMaxLength(500).IsRequired();
        builder.Property(b => b.Author).HasMaxLength(300).IsRequired();
        builder.Property(b => b.CoverUrl).HasMaxLength(2000);
    }
}
