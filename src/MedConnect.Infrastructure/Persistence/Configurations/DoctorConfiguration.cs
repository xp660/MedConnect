using MedConnect.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedConnect.Infrastructure.Persistence.Configurations;

public class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
{
    public void Configure(EntityTypeBuilder<Doctor> builder)
    {
        builder.ToTable("doctors");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(d => d.FullName).HasColumnName("full_name").HasMaxLength(128).IsRequired();
        builder.Property(d => d.Specialty).HasColumnName("specialty").HasMaxLength(64).IsRequired();
        builder.Property(d => d.CreatedAtUtc).HasColumnName("created_at").HasColumnType("datetime(6)").IsRequired();

        builder.HasIndex(d => d.Specialty).HasDatabaseName("ix_doctors_specialty");
    }
}
