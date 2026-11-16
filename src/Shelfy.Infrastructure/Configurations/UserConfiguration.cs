using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shelfy.Domain;

namespace Shelfy.Infrastructure.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.IdentitySubject).HasMaxLength(200);
        builder.HasIndex(u => u.IdentitySubject).IsUnique();
        builder.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.City).HasMaxLength(100).IsRequired();
        builder.Property(u => u.MailingAddress).HasMaxLength(500);
    }
}
