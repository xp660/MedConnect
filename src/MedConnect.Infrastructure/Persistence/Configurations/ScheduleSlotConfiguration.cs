using MedConnect.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedConnect.Infrastructure.Persistence.Configurations;

public class ScheduleSlotConfiguration : IEntityTypeConfiguration<ScheduleSlot>
{
    public void Configure(EntityTypeBuilder<ScheduleSlot> builder)
    {
        builder.ToTable("schedule_slots", tb =>
        {
            tb.HasCheckConstraint("ck_slot_capacity", "capacity >= 1");
            tb.HasCheckConstraint("ck_slot_booked", "booked_count >= 0 AND booked_count <= capacity");
            tb.HasCheckConstraint("ck_slot_time_order", "start_time_utc < end_time_utc");
        });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedOnAdd();

        builder.Property(s => s.DoctorId).HasColumnName("doctor_id").IsRequired();
        builder.Property(s => s.Capacity).HasColumnName("capacity").IsRequired();
        builder.Property(s => s.BookedCount).HasColumnName("booked_count").HasDefaultValue(0).IsRequired();
        builder.Property(s => s.Status).HasColumnName("status").HasConversion<byte>().HasColumnType("tinyint").IsRequired();
        builder.Property(s => s.Version).HasColumnName("version").HasDefaultValue(0).IsConcurrencyToken().IsRequired();
        builder.Property(s => s.CreatedAtUtc).HasColumnName("created_at").HasColumnType("datetime(6)").IsRequired();
        builder.Property(s => s.UpdatedAtUtc).HasColumnName("updated_at").HasColumnType("datetime(6)").IsRequired();

        builder.ComplexProperty(s => s.TimeSlot, ts =>
        {
            ts.Property(t => t.StartUtc).HasColumnName("start_time_utc").HasColumnType("datetime(6)").IsRequired();
            ts.Property(t => t.EndUtc).HasColumnName("end_time_utc").HasColumnType("datetime(6)").IsRequired();
        });

        // ux_slot_doctor_start / ix_slot_doctor_time 混合了 DoctorId（一般欄位）與 TimeSlot.StartUtc
        // （EF Core 9 Complex Type 的屬性）。EF Core 目前不允許 Complex Type 屬性參與 composite index
        // ——HasIndex() 的 lambda/字串路徑 overload 都會在 design-time 丟例外，底層
        // IMutableEntityType.AddIndex() 也會直接拒絕「index 屬性必須全部屬於同一個 entity type」。
        // 見 https://github.com/dotnet/efcore/issues/31411。這兩個 index 改在 migration 用原始 SQL
        // 補上（見 Persistence/Migrations/*_InitialCreate.cs），此處不用 Fluent API 宣告。

        builder.HasOne<Doctor>()
            .WithMany()
            .HasForeignKey(s => s.DoctorId)
            .HasConstraintName("fk_slot_doctor")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
