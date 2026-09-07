using MedConnect.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedConnect.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(u => u.Email).HasColumnName("email").HasMaxLength(256).IsRequired();
        builder.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(256).IsRequired();
        builder.Property(u => u.Role).HasColumnName("role").HasConversion<byte>().HasColumnType("tinyint").IsRequired();
        builder.Property(u => u.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(u => u.CreatedAtUtc).HasColumnName("created_at").HasColumnType("datetime(6)").IsRequired();

        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName("ux_users_email");
    }
}
