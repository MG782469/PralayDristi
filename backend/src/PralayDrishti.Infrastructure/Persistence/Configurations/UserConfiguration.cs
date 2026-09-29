using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PralayDrishti.Domain.Entities;

namespace PralayDrishti.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Email).IsRequired().HasMaxLength(256);
        b.Property(x => x.NormalizedEmail).IsRequired().HasMaxLength(256);
        b.Property(x => x.FullName).IsRequired().HasMaxLength(160);
        b.Property(x => x.PasswordHash).IsRequired().HasMaxLength(512);
        b.Property(x => x.Role).IsRequired().HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.CreatedUtc).IsRequired();
        b.HasIndex(x => x.NormalizedEmail).IsUnique();

        b.HasMany(x => x.RefreshTokens).WithOne(t => t.User).HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.RefreshTokens).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TokenHash).IsRequired().HasMaxLength(128);
        b.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
        b.Property(x => x.CreatedByIp).HasMaxLength(64);
        b.Property(x => x.RevokedByIp).HasMaxLength(64);
        b.Property(x => x.RevokeReason).HasMaxLength(40);
        b.Property(x => x.ExpiresUtc).IsRequired();
        b.Property(x => x.CreatedUtc).IsRequired();
        b.Ignore(x => x.IsRevoked);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.UserId);
    }
}
