using MedConnect.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedConnect.Infrastructure.Persistence.Configurations;

public class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("patients");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(p => p.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(p => p.FullName).HasColumnName("full_name").HasMaxLength(128).IsRequired();
        builder.Property(p => p.CreatedAtUtc).HasColumnName("created_at").HasColumnType("datetime(6)").IsRequired();

        builder.HasIndex(p => p.UserId).IsUnique().HasDatabaseName("ux_patients_user");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .HasConstraintName("fk_patients_user")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
