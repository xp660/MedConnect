using MedConnect.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedConnect.Infrastructure.Persistence.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(a => a.SlotId).HasColumnName("slot_id").IsRequired();
        builder.Property(a => a.PatientId).HasColumnName("patient_id").IsRequired();
        builder.Property(a => a.Status).HasColumnName("status").HasConversion<byte>().HasColumnType("tinyint").IsRequired();
        builder.Property(a => a.BookedAtUtc).HasColumnName("booked_at_utc").HasColumnType("datetime(6)").IsRequired();
        builder.Property(a => a.CancelledAtUtc).HasColumnName("cancelled_at_utc").HasColumnType("datetime(6)");
        builder.Property(a => a.Version).HasColumnName("version").HasDefaultValue(0).IsConcurrencyToken().IsRequired();

        // MySQL 無 partial/filtered index：用 generated column 模擬「同一 slot 同一病人僅一筆有效(未取消)預約」
        builder.Property<long?>("ActivePatientId")
            .HasColumnName("active_patient_id")
            .HasComputedColumnSql("IF(status = 0, patient_id, NULL)", stored: true);

        builder.HasIndex(a => a.SlotId).HasDatabaseName("ix_appt_slot");
        builder.HasIndex(a => new { a.PatientId, a.Status, a.BookedAtUtc }).HasDatabaseName("ix_appt_patient_status");
        builder.HasIndex("SlotId", "ActivePatientId").IsUnique().HasDatabaseName("ux_appt_slot_active_patient");

        builder.HasOne<ScheduleSlot>()
            .WithMany()
            .HasForeignKey(a => a.SlotId)
            .HasConstraintName("fk_appt_slot")
            .OnDelete(DeleteBehavior.Restrict);

        // 必須是 Restrict：active_patient_id 這個 STORED generated column 依賴 patient_id，
        // MySQL 不允許「被 generated column 依賴的欄位」所在的 FK 使用 ON DELETE CASCADE/SET NULL，
        // 否則 CREATE TABLE 會丟 Error 1215 Cannot add foreign key constraint（已用真的 MySQL 驗證過）。
        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(a => a.PatientId)
            .HasConstraintName("fk_appt_patient")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
