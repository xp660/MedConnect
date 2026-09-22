using FluentAssertions;
using MediatR;
using MedConnect.Application.Appointments.Commands.BookAppointment;
using MedConnect.Application.Common.Exceptions;
using MedConnect.Domain.Entities;
using MedConnect.Domain.Enums;
using MedConnect.Domain.ValueObjects;
using MedConnect.Infrastructure.Persistence;
using MedConnect.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace MedConnect.IntegrationTests.Appointments;

/// <summary>
/// architecture-plan.md §8.5「無 partial commit」的專門驗證。
///
/// 為了避開 InnoDB 死結，Booking 被拆成同一筆交易內的兩次 SaveChanges：
/// 先 UPDATE schedule_slots（拿 X 鎖），再 INSERT appointments（做 FK 檢查）。
/// 這個順序帶來一個必須被證明的新風險：**第一次 SaveChanges 成功之後，第二次才失敗**。
/// 如果交易沒有正確回滾，schedule_slots.booked_count 就會被推進、卻沒有對應的 appointment，
/// 兩張表從此 drift，而且是永久性的髒資料。
///
/// 重複預約剛好是這個情境的天然重現方式：capacity 還有空位（所以 UPDATE 會成功），
/// 但同一個病人已經有一筆有效預約（所以 INSERT 一定撞 ux_appt_slot_active_patient）。
/// </summary>
[Collection(MySqlContainerCollection.Name)]
public class BookingTransactionRollbackTests
{
    private readonly MySqlContainerFixture _fixture;
    private readonly ITestOutputHelper _output;

    public BookingTransactionRollbackTests(MySqlContainerFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task When_insert_fails_after_a_successful_slot_update_the_whole_transaction_rolls_back()
    {
        var (slotId, patientId) = await SeedSlotAndSinglePatientAsync();

        // 第一次預約：正常成功，booked_count 0 → 1、version 0 → 1。
        await using (var firstScope = _fixture.ScopeFactory.CreateAsyncScope())
        {
            await firstScope.ServiceProvider.GetRequiredService<IMediator>()
                .Send(new BookAppointmentCommand(patientId, slotId));
        }

        var afterFirst = await ReadSlotStateAsync(slotId);
        _output.WriteLine($"after 1st booking: booked_count={afterFirst.BookedCount} version={afterFirst.Version} appointments={afterFirst.ActiveAppointments}");
        afterFirst.BookedCount.Should().Be(1);
        afterFirst.ActiveAppointments.Should().Be(1);

        // 第二次預約（同一個病人、同一個時段）：capacity 是 5，所以 domain 檢查會過、
        // schedule_slots 的 UPDATE 也會成功（version 1 對得上），但接著的 INSERT 必然
        // 撞上唯一索引。這正是「UPDATE 已落地、INSERT 才失敗」的那個危險視窗。
        await using (var secondScope = _fixture.ScopeFactory.CreateAsyncScope())
        {
            var act = async () => await secondScope.ServiceProvider.GetRequiredService<IMediator>()
                .Send(new BookAppointmentCommand(patientId, slotId));

            await act.Should().ThrowAsync<DuplicateBookingException>();
        }

        // 關鍵斷言：用全新的 scope/連線重讀，確認第一次 SaveChanges 的 UPDATE 被完整退回去，
        // booked_count 與 version 都停在第一次預約後的值，沒有被第二次失敗的交易推進。
        var afterSecond = await ReadSlotStateAsync(slotId);
        _output.WriteLine($"after failed 2nd booking: booked_count={afterSecond.BookedCount} version={afterSecond.Version} appointments={afterSecond.ActiveAppointments}");

        afterSecond.BookedCount.Should().Be(afterFirst.BookedCount,
            "INSERT 失敗後，已經成功送出的 UPDATE 必須跟著回滾，否則 booked_count 會憑空多一");
        afterSecond.Version.Should().Be(afterFirst.Version,
            "version 也是同一筆 UPDATE 寫的，回滾後不該被推進");
        afterSecond.ActiveAppointments.Should().Be(afterFirst.ActiveAppointments,
            "兩張表必須一致，不能有 drift");
    }

    private async Task<(int BookedCount, int Version, int ActiveAppointments)> ReadSlotStateAsync(long slotId)
    {
        await using var scope = _fixture.ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();

        var slot = await db.ScheduleSlots.AsNoTracking().SingleAsync(s => s.Id == slotId);
        var active = await db.Appointments.AsNoTracking()
            .CountAsync(a => a.SlotId == slotId && a.Status == AppointmentStatus.Booked);

        return (slot.BookedCount, slot.Version, active);
    }

    private async Task<(long SlotId, long PatientId)> SeedSlotAndSinglePatientAsync()
    {
        await using var scope = _fixture.ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var doctor = new Doctor("Rollback Test Doctor", "Dermatology", now);
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();

        // capacity 刻意留很多空位，確保第二次預約失敗的原因是唯一索引、不是 SlotFull。
        var start = now.UtcDateTime.AddDays(2);
        var slot = new ScheduleSlot(doctor.Id, new TimeSlot(start, start.AddMinutes(30)), capacity: 5, now);
        db.ScheduleSlots.Add(slot);
        await db.SaveChangesAsync();

        var user = new User("rollback-patient@rollback.test", "not-a-real-password-hash", UserRole.Patient, now);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var patient = new Patient(user.Id, "Rollback Patient", now);
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        return (slot.Id, patient.Id);
    }
}
