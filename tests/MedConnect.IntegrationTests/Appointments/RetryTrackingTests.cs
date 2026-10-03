using FluentAssertions;
using MediatR;
using MedConnect.Application.Abstractions;
using MedConnect.Application.Appointments.Commands.BookAppointment;
using MedConnect.Domain.Entities;
using MedConnect.Domain.Enums;
using MedConnect.Domain.ValueObjects;
using MedConnect.Infrastructure.Persistence;
using MedConnect.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace MedConnect.IntegrationTests.Appointments;

/// <summary>
/// RetryBehavior 與它包住的 Handler 共用同一個 Scoped DbContext（不像併發測試每個請求各自 CreateScope）。
/// 這組測試用真實 MySQL 證明：同一個 scope 內重讀，change tracker 會回傳第一次追蹤到的舊物件，
/// 不是資料庫的最新資料——這是「retry 會不斷重複同樣失敗」的根因，不是只憑分析的假設。
/// 修法是 IUnitOfWork.ResetTracking()（architecture-plan.md §0 v1.9）。
/// </summary>
[Collection(MySqlContainerCollection.Name)]
public class RetryTrackingTests
{
    private readonly MySqlContainerFixture _fixture;

    public RetryTrackingTests(MySqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Rereading_in_the_same_scope_without_ResetTracking_returns_the_stale_tracked_instance()
    {
        var (slotId, patientIds) = await SeedSlotAndPatientsAsync(capacity: 5, patientCount: 1);

        await using var scope = _fixture.ScopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IScheduleSlotRepository>();

        var firstRead = await repository.GetByIdAsync(slotId, CancellationToken.None);
        firstRead!.BookedCount.Should().Be(0);

        // 別的 scope（= 別的請求）在這之間訂走一個名額，資料庫現在是 booked_count=1、version=1。
        await BookInAnotherScopeAsync(patientIds[0], slotId);

        var secondRead = await repository.GetByIdAsync(slotId, CancellationToken.None);

        // 這條斷言證明的是「風險存在」：同一個 scope 再讀一次，拿到的是同一個舊物件、數字沒變。
        secondRead.Should().BeSameAs(firstRead, "change tracker 對已追蹤的主鍵直接回傳記憶體裡的實例");
        secondRead!.BookedCount.Should().Be(0, "它不會用資料庫的新值覆蓋，所以讀到的是過期資料");
        secondRead.Version.Should().Be(0);
    }

    [Fact]
    public async Task Rereading_after_ResetTracking_sees_the_latest_database_state()
    {
        var (slotId, patientIds) = await SeedSlotAndPatientsAsync(capacity: 5, patientCount: 1);

        await using var scope = _fixture.ScopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IScheduleSlotRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var firstRead = await repository.GetByIdAsync(slotId, CancellationToken.None);
        firstRead!.BookedCount.Should().Be(0);

        await BookInAnotherScopeAsync(patientIds[0], slotId);

        unitOfWork.ResetTracking();
        var secondRead = await repository.GetByIdAsync(slotId, CancellationToken.None);

        secondRead.Should().NotBeSameAs(firstRead, "ResetTracking 之後必須是從資料庫重新載入的新實例");
        secondRead!.BookedCount.Should().Be(1, "必須看到別的請求已經寫入的最新資料");
        secondRead.Version.Should().Be(1);
    }

    // ---------- helpers ----------

    private async Task BookInAnotherScopeAsync(long patientId, long slotId)
    {
        await using var otherScope = _fixture.ScopeFactory.CreateAsyncScope();
        await otherScope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new BookAppointmentCommand(patientId, slotId));
    }

    private async Task<(long SlotId, long[] PatientIds)> SeedSlotAndPatientsAsync(int capacity, int patientCount)
    {
        await using var scope = _fixture.ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var doctor = new Doctor("Retry Tracking Doctor", "Cardiology", now);
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();

        var start = now.UtcDateTime.AddDays(1);
        var slot = new ScheduleSlot(doctor.Id, new TimeSlot(start, start.AddMinutes(30)), capacity, now);
        db.ScheduleSlots.Add(slot);
        await db.SaveChangesAsync();

        var tag = Guid.NewGuid().ToString("N")[..12];
        var users = Enumerable.Range(1, patientCount)
            .Select(i => new User($"{tag}-{i}@retry-tracking.test", "not-a-real-password-hash", UserRole.Patient, now))
            .ToArray();
        db.Users.AddRange(users);
        await db.SaveChangesAsync();

        var patients = users.Select((u, i) => new Patient(u.Id, $"Retry Patient {i + 1}", now)).ToArray();
        db.Patients.AddRange(patients);
        await db.SaveChangesAsync();

        return (slot.Id, patients.Select(p => p.Id).ToArray());
    }
}
