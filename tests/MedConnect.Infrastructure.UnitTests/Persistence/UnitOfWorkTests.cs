using System.Reflection;
using FluentAssertions;
using MedConnect.Application.Common.Exceptions;
using MedConnect.Domain.Entities;
using MedConnect.Domain.ValueObjects;
using MedConnect.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using MySqlConnector;
using Xunit;

namespace MedConnect.Infrastructure.UnitTests.Persistence;

/// <summary>
/// 回歸測試：UnitOfWork.IsDuplicateActivePatientBooking() 曾經假設「觸發唯一索引衝突的那次
/// SaveChanges 只會有一個 entry（就是那筆新增的 Appointment）」，但 BookAppointmentHandler
/// 一定會同時讓 ScheduleSlot 變成 Modified（BookedCount++）並新增一筆 Appointment，兩個 entry
/// 本來就會一起出現在同一次 SaveChanges——這個假設在 1c 手動用真 MySQL 驗證時被戳破：實際跑
/// 起來 slotId/patientId 都被誤判成 0。
///
/// 這裡不需要真的連 MySQL：用一個獨立、不套用 MedConnectDbContext 那些 MySQL 專屬設定的最小
/// DbContext 重現「一次 SaveChanges 有兩個 entries」的情境，並用反射建構一個真正的
/// MySqlConnector.MySqlException（它所有建構子都是 non-public，正式 driver 程式碼以外沒有其他
/// 方式能拿到真的實例，這是唯一需要反射的地方，不是要繞過什麼，純粹是這個型別本來就設計成
/// 只給 driver 自己 new）。
/// </summary>
public class UnitOfWorkTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>只用來取得真的 EntityEntry，刻意不套用 MedConnectDbContext 的 MySQL 專屬設定。</summary>
    private class EntryFactoryDbContext(DbContextOptions<EntryFactoryDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Appointment>().HasKey(a => a.Id);
            modelBuilder.Entity<ScheduleSlot>(b =>
            {
                b.HasKey(s => s.Id);
                b.Ignore(s => s.TimeSlot);
            });
        }
    }

    /// <summary>SaveChangesAsync 直接丟出預先準備好的例外，不需要真的連任何資料庫。</summary>
    private sealed class ThrowingDbContext(DbContextOptions<MedConnectDbContext> options, Exception exceptionToThrow)
        : MedConnectDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw exceptionToThrow;
    }

    private static MySqlException CreateDuplicateKeyMySqlException(string message)
    {
        var errorCodeType = typeof(MySqlException).Assembly.GetType("MySqlConnector.MySqlErrorCode")!;
        var duplicateKeyEntry = Enum.Parse(errorCodeType, "DuplicateKeyEntry");
        var ctor = typeof(MySqlException).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance, null, [errorCodeType, typeof(string)], null)!;

        return (MySqlException)ctor.Invoke([duplicateKeyEntry, message]);
    }

    private static async Task<List<EntityEntry>> CreateAddedAppointmentAndModifiedSlotEntriesAsync(Appointment appointment, ScheduleSlot slot)
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<EntryFactoryDbContext>().UseSqlite(connection).Options;
        using var context = new EntryFactoryDbContext(options);
        await context.Database.EnsureCreatedAsync();

        // slot 要先真的存過一次拿到資料庫產生的 Id，才能把它標成 Modified——
        // EF 不允許把「Id 還是預設值 0（代表尚未指派）」的 entity 直接標成 Modified。
        context.Add(slot);
        await context.SaveChangesAsync();

        var appointmentEntry = context.Add(appointment);
        var slotEntry = context.Update(slot);

        // 用完就把 context 丟掉，但 EntityEntry 物件本身可以在 context Dispose 後繼續讀取狀態，
        // 這裡只需要它「曾經是哪個 State、包了哪個 Entity」這兩個唯讀資訊。
        return [appointmentEntry, slotEntry];
    }

    [Fact]
    public async Task SaveChangesAsync_WhenDuplicateKeyConflictHasBothAppointmentAndSlotEntries_StillIdentifiesRealSlotIdAndPatientId()
    {
        var slot = new ScheduleSlot(doctorId: 1, new TimeSlot(Now.UtcDateTime.AddHours(1), Now.UtcDateTime.AddHours(1).AddMinutes(30)), capacity: 5, Now);
        var appointment = new Appointment(slotId: 1, patientId: 7, Now);

        var entries = await CreateAddedAppointmentAndModifiedSlotEntriesAsync(appointment, slot);
        entries.Should().HaveCount(2, "Booking 一定會同時讓 ScheduleSlot 變 Modified、新增 Appointment，這是測試要重現的前提");

        var mySqlException = CreateDuplicateKeyMySqlException("Duplicate entry '1-7' for key 'appointments.ux_appt_slot_active_patient'");
        var dbUpdateException = new DbUpdateException("simulated duplicate key violation", mySqlException, entries);

        var contextOptions = new DbContextOptionsBuilder<MedConnectDbContext>().UseSqlite("DataSource=:memory:").Options;
        using var throwingContext = new ThrowingDbContext(contextOptions, dbUpdateException);
        var unitOfWork = new UnitOfWork(throwingContext);

        var act = async () => await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<DuplicateBookingException>();
        thrown.Which.ScheduleSlotId.Should().Be(1);
        thrown.Which.PatientId.Should().Be(7);
    }
}
