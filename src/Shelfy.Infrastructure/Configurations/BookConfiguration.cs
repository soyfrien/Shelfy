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
        builder.Property(b => b.Isbn13).HasMaxLength(13);
        builder.HasIndex(b => b.Isbn13).IsUnique(); // btree for exact ISBN-13 hits; NULLs allowed multiple times
        builder.Property(b => b.Title).HasMaxLength(500).IsRequired();
        builder.Property(b => b.Author).HasMaxLength(300).IsRequired();
        builder.Property(b => b.CoverUrl).HasMaxLength(2000);
        // PostgreSQL also gets pg_trgm GIN indexes on lower(Title) and lower(Author); see the AddBookSearch migration.
    }
}
