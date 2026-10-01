using FluentAssertions;
using MedConnect.Application.Abstractions;
using MedConnect.Application.ScheduleSlots.Queries.GetAvailableSlots;
using MedConnect.Domain.Entities;
using MedConnect.Domain.ValueObjects;
using MedConnect.Infrastructure.Persistence;
using MedConnect.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedConnect.IntegrationTests.ScheduleSlots;

/// <summary>
/// 直接測 IScheduleSlotQueryRepository 的投影邏輯，不經過 HTTP——驗證 AvailableCount
/// 的計算、Closed/其他醫生/其他日期的排除，都是真的在 MySQL 上算出來的，不是 InMemory
/// provider 那種「全部通過但毫無意義」的綠燈（architecture-plan.md §8.4）。
/// </summary>
[Collection(MySqlContainerCollection.Name)]
public class ScheduleSlotQueryRepositoryTests
{
    private readonly MySqlContainerFixture _fixture;

    public ScheduleSlotQueryRepositoryTests(MySqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_ExcludesClosedSlotsAndOtherDoctorsAndOtherDates()
    {
        var (doctorId, date) = await SeedAsync();

        var results = await QueryAsync(doctorId, date);

        // 被排除的三種情況都刻意種了一筆對照組：Closed、別的醫生、別的日期。
        // 只斷言「結果裡沒有它們」還不夠有說服力，下一個測試另外驗證 AvailableCount 算對了。
        results.Should().OnlyContain(s => s.DoctorId == doctorId);
        results.Select(s => s.Status).Should().OnlyContain(status => status == "Open");
        results.Select(s => s.StartUtc.Date).Should().OnlyContain(d => d == date.ToDateTime(TimeOnly.MinValue));
    }

    [Fact]
    public async Task GetAvailableSlotsAsync_ComputesAvailableCountAsCapacityMinusBookedCount_IncludingFullyBookedSlots()
    {
        var (doctorId, date) = await SeedAsync();

        var results = await QueryAsync(doctorId, date);

        var partiallyBooked = results.Should().ContainSingle(s => s.Capacity == 5).Subject;
        partiallyBooked.AvailableCount.Should().Be(3, "Capacity=5、已訂 2 個，應剩 3 個名額");

        // 已滿的診次（AvailableCount == 0）也必須出現在結果裡，不可被悄悄濾掉——
        // 讓前端自己決定怎麼呈現「額滿」，而不是由這層幫它做決定。
        var fullyBooked = results.Should().ContainSingle(s => s.Capacity == 3).Subject;
        fullyBooked.AvailableCount.Should().Be(0, "Capacity=3、已訂滿 3 個，應剩 0 個名額，但仍要回傳");
    }

    /// <summary>
    /// 刻意用全新的 scope 執行查詢（跟 ConcurrentBookingTests 的「重讀用新 scope」同理），
    /// 不沿用 SeedAsync 裡已經 Dispose 掉的那個 scope/DbContext。
    /// </summary>
    private async Task<List<ScheduleSlotDto>> QueryAsync(long doctorId, DateOnly date)
    {
        await using var scope = _fixture.ScopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IScheduleSlotQueryRepository>();
        return await repository.GetAvailableSlotsAsync(doctorId, date, CancellationToken.None);
    }

    private async Task<(long DoctorId, DateOnly Date)> SeedAsync()
    {
        await using var scope = _fixture.ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var doctor = new Doctor("Query Test Doctor", "Cardiology", now);
        var otherDoctor = new Doctor("Other Doctor", "Dermatology", now);
        db.Doctors.AddRange(doctor, otherDoctor);
        await db.SaveChangesAsync();

        var targetDate = DateOnly.FromDateTime(now.UtcDateTime.Date.AddDays(1));
        var targetDayStart = targetDate.ToDateTime(TimeOnly.MinValue);

        // 部分已滿：Capacity=5，先在記憶體內 Book() 兩次再存檔，讓 BookedCount=2 隨著 INSERT 落地
        // （不需要真的建立對應的 Appointment 列：查詢端只讀 schedule_slots 自己的 Capacity/BookedCount，
        // 這兩個欄位本來就是 ScheduleSlot Aggregate 自己持有的 invariant，見 architecture-plan.md §3.3）。
        var partiallyBookedSlot = new ScheduleSlot(doctor.Id, new TimeSlot(targetDayStart.AddHours(9), targetDayStart.AddHours(9).AddMinutes(30)), capacity: 5, now);
        partiallyBookedSlot.Book(now);
        partiallyBookedSlot.Book(now);
        db.ScheduleSlots.Add(partiallyBookedSlot);

        // 全滿：Capacity=3，Book() 三次打滿。
        var fullyBookedSlot = new ScheduleSlot(doctor.Id, new TimeSlot(targetDayStart.AddHours(10), targetDayStart.AddHours(10).AddMinutes(30)), capacity: 3, now);
        fullyBookedSlot.Book(now);
        fullyBookedSlot.Book(now);
        fullyBookedSlot.Book(now);
        db.ScheduleSlots.Add(fullyBookedSlot);

        // 對照組 1：同一天、同一醫生，但 Closed——必須被排除。
        var closedSlot = new ScheduleSlot(doctor.Id, new TimeSlot(targetDayStart.AddHours(11), targetDayStart.AddHours(11).AddMinutes(30)), capacity: 4, now);
        closedSlot.Close(now);
        db.ScheduleSlots.Add(closedSlot);

        // 對照組 2：同一天，但別的醫生——必須被排除。
        var otherDoctorSlot = new ScheduleSlot(otherDoctor.Id, new TimeSlot(targetDayStart.AddHours(9), targetDayStart.AddHours(9).AddMinutes(30)), capacity: 2, now);
        db.ScheduleSlots.Add(otherDoctorSlot);

        // 對照組 3：同一醫生，但別的日期——必須被排除。
        var otherDateStart = targetDayStart.AddDays(1).AddHours(9);
        var otherDateSlot = new ScheduleSlot(doctor.Id, new TimeSlot(otherDateStart, otherDateStart.AddMinutes(30)), capacity: 2, now);
        db.ScheduleSlots.Add(otherDateSlot);

        await db.SaveChangesAsync();

        return (doctor.Id, targetDate);
    }
}
