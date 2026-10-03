using FluentAssertions;
using MediatR;
using MedConnect.Application.Appointments.Commands.BookAppointment;
using MedConnect.Application.Common.Exceptions;
using MedConnect.Domain.Entities;
using MedConnect.Domain.Enums;
using MedConnect.Domain.Exceptions;
using MedConnect.Domain.ValueObjects;
using MedConnect.Infrastructure.Persistence;
using MedConnect.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace MedConnect.IntegrationTests.Appointments;

/// <summary>
/// architecture-plan.md §8.5 的招牌測試：50 個不同病人同時搶一個 Capacity=5 的時段。
///
/// 為什麼一定要「不同」病人：同一個病人重複送會先撞上 ux_appt_slot_active_patient 唯一索引，
/// 拿到的是 DUPLICATE_BOOKING，根本走不到樂觀鎖那一步，等於沒測到要測的東西。
///
/// v2（有 bounded retry，architecture-plan.md §0 v1.9）的斷言說明：
///
/// §8.5 原文要求 BookedCount == min(N, Capacity)。v1（無 retry）達不到（實測 1～3 個，見 §0 v1.4），
/// v2 加了 RetryBehavior 後 60 輪實測全為 5——但那是機率證據、不是保證：有界重試（3 次）下，
/// 一個請求仍可能連輸 4 輪而以 RETRY_EXHAUSTED 收場，所以精確值斷言會是 flaky test。
/// 經決策（§0 v1.9 第 9 項）：**不收緊成 == 5，改斷言下限 >= Capacity - 1**，並把實際成功數與
/// RETRY_EXHAUSTED 的次數／比例印出來，作為持續觀察的健康指標（趨勢給人看，不當作失敗條件）。
///
/// RETRY_EXHAUSTED 是新的「正常失敗型態」：重試用盡仍衝突，是預期內的 409，不是 bug，所以和
/// SLOT_FULL / CONCURRENCY_CONFLICT 一樣被 catch 成資料。其餘任何例外仍故意不接。
///
/// 所以這個測試證明的是：**不會超賣、不會有 partial commit、不會有非預期例外、不會過度保守
/// （成功數 >= Capacity - 1）**，而不是「一定賣得掉 5 個」。
/// </summary>
[Collection(MySqlContainerCollection.Name)]
public class ConcurrentBookingTests
{
    private const int SlotCapacity = 5;
    private const int ConcurrentPatients = 50;

    private readonly MySqlContainerFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ConcurrentBookingTests(MySqlContainerFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task Fifty_patients_booking_one_slot_with_capacity_five_should_never_oversell_and_never_drift()
    {
        // ---------- Arrange ----------
        var (slotId, patientIds) = await SeedSlotAndPatientsAsync();

        // ---------- Act ----------
        // TaskCompletionSource 當閘門（§8.5）：先讓 50 個 Task 都排程好、停在同一條線上，
        // 再一次放行。否則它們會被逐一排程成近似循序執行，競爭窗口幾乎為零，測試就算過了也
        // 沒有證明力。RunContinuationsAsynchronously 避免所有 continuation 擠在 SetResult()
        // 的那一條執行緒上被同步跑完（那又會退化成循序）。
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = patientIds
            .Select(patientId => Task.Run(async () =>
            {
                await gate.Task;

                // scope 一定要開在迴圈「裡面」：每個模擬請求都要有自己的 DbContext，
                // 就像 ASP.NET Core 每個 HTTP request 一個 scope 一樣。若共用一個 DbContext，
                // 50 個請求會共用同一份 change tracker 與同一個 ScheduleSlot 實例，
                // 測到的會是 EF 的記憶體行為，不是資料庫層的樂觀鎖。
                await using var scope = _fixture.ScopeFactory.CreateAsyncScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

                try
                {
                    await mediator.Send(new BookAppointmentCommand(patientId, slotId));
                    return new BookingAttemptResult(patientId, Success: true, ErrorCode: null);
                }
                // 只精準攔截這兩種「預期內的失敗」。其餘任何例外都故意不接，讓它往外拋、
                // 讓測試紅燈並帶出完整 stack trace——catch (Exception) 包底會把真正的 bug
                // （例如 FK 設定錯、連線耗盡、handler 拋 NullReference）偽裝成一次普通的
                // 併發失敗，測試照樣綠燈，那就成了假測試。
                catch (SlotFullException)
                {
                    return new BookingAttemptResult(patientId, Success: false, ErrorCode: "SLOT_FULL");
                }
                catch (ConcurrencyConflictException)
                {
                    return new BookingAttemptResult(patientId, Success: false, ErrorCode: "CONCURRENCY_CONFLICT");
                }
                catch (RetryExhaustedException)
                {
                    return new BookingAttemptResult(patientId, Success: false, ErrorCode: "RETRY_EXHAUSTED");
                }
            }))
            .ToArray();

        gate.SetResult();

        var results = await Task.WhenAll(attempts);

        // ---------- Assert ----------
        var succeeded = results.Where(r => r.Success).ToArray();
        var failed = results.Where(r => !r.Success).ToArray();

        _output.WriteLine($"total={results.Length} succeeded={succeeded.Length} failed={failed.Length}");
        foreach (var group in failed.GroupBy(r => r.ErrorCode).OrderBy(g => g.Key))
        {
            _output.WriteLine($"  {group.Key}: {group.Count()}");
        }

        // 健康指標（只印出、不斷言）：RETRY_EXHAUSTED 的次數與佔總請求的比例。
        // 長期趨勢上升代表重試參數（次數／延遲／jitter）或鎖競爭出了問題，值得人工檢視。
        var exhausted = failed.Count(r => r.ErrorCode == "RETRY_EXHAUSTED");
        _output.WriteLine(
            $"[health] RETRY_EXHAUSTED={exhausted}/{results.Length} ({(double)exhausted / results.Length:P1})");

        // 沒有請求被憑空遺漏或重複計算：50 個 Task 進去，50 個結果出來，
        // 每個結果不是成功就是失敗，兩者互斥、合起來剛好覆蓋全部。
        results.Should().HaveCount(ConcurrentPatients, "每個請求都必須回報結果，不能有人默默消失");
        (succeeded.Length + failed.Length).Should().Be(ConcurrentPatients,
            "成功與失敗互斥且窮盡，不能有請求被重複計算或遺漏");

        // v2（有 bounded retry）的期望值：不超賣，且幾乎填滿；不斷言恰好 5——理由見 class 註解。
        // 下限用 Capacity - 1：有界重試不保證必然填滿，但退到 4 以下代表重試機制或鎖定順序出了問題。
        succeeded.Should().HaveCountGreaterThanOrEqualTo(SlotCapacity - 1,
            "有 retry 時成功數應接近 Capacity；遠低於它代表重試沒有發揮作用或過度保守");
        succeeded.Should().HaveCountLessThanOrEqualTo(SlotCapacity, "成功數不可能超過 capacity，超過就是超賣");

        // 死結（MySqlError 1213）目前完全沒有被 UnitOfWork.SaveChangesAsync 攔截、轉譯
        // （見該檔案：只認得 DbUpdateConcurrencyException 與 1062 duplicate key）。如果它
        // 曾經以任何形式漏出來，並不會被誤算進下面這兩種已知的 errorCode 裡——它會是一個
        // 完全不同的例外型別，連本測試的 catch (SlotFullException) / catch
        // (ConcurrencyConflictException) 都接不住，會讓上面的 Task.WhenAll 直接把它原樣拋出、
        // 測試在跑到這裡之前就已經紅燈，不會被這條斷言悄悄吞掉、也不會偽裝成第三種 errorCode。
        // 換句話說：底下這條斷言能執行到，本身就代表這一輪測試裡沒有任何一個死結漏網。
        failed.Should().OnlyContain(
            r => r.ErrorCode == "SLOT_FULL" || r.ErrorCode == "CONCURRENCY_CONFLICT" || r.ErrorCode == "RETRY_EXHAUSTED",
            "失敗只能來自這三個已知原因；出現別的代表有非預期的失敗路徑");

        succeeded.Select(r => r.PatientId).Should().OnlyHaveUniqueItems(
            "同一個病人不可能佔到兩個名額");

        // 用全新的 scope / DbContext / 連線重讀（§8.5）：MySQL 預設 REPEATABLE READ，
        // 沿用前面任何一個請求的 DbContext 重讀都可能拿到那個 transaction 開始時的快照，
        // 斷言就會建立在過期資料上。
        await using var verifyScope = _fixture.ScopeFactory.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MedConnectDbContext>();

        var finalSlot = await verifyDb.ScheduleSlots.AsNoTracking().SingleAsync(s => s.Id == slotId);
        var activeAppointmentCount = await verifyDb.Appointments.AsNoTracking()
            .CountAsync(a => a.SlotId == slotId && a.Status == AppointmentStatus.Booked);
        var activeAppointments = await verifyDb.Appointments.AsNoTracking()
            .Where(a => a.SlotId == slotId && a.Status == AppointmentStatus.Booked)
            .ToListAsync();

        _output.WriteLine(
            $"DB final state: booked_count={finalSlot.BookedCount} capacity={finalSlot.Capacity} " +
            $"version={finalSlot.Version} active_appointments={activeAppointmentCount}");

        finalSlot.BookedCount.Should().BeLessThanOrEqualTo(finalSlot.Capacity, "不可超賣");

        // 資料庫內部自洽：schedule_slots.booked_count 必須等於 appointments 裡 Status == Booked
        // 的實際筆數。這條不因「最終賣出幾個」而打折扣——不管成功數落在 1～5 的哪個值，
        // 兩張表永遠不可以對不上；對不上就是 partial commit / drift，是比「賣不到 5 個」
        // 嚴重得多的問題。刻意用獨立算出來的 activeAppointmentCount 比對，不透過 succeeded.Length，
        // 避免斷言本身繞了一圈又回到「相信回應」而漏掉真正該查的 DB 狀態。
        finalSlot.BookedCount.Should().Be(activeAppointmentCount,
            "schedule_slots.booked_count 必須與 appointments 表中實際 Booked 筆數完全一致，不能有 drift");

        // 回應與 DB 也要一致：Handler 回報成功的人數，必須等於資料庫實際記下的名額數。
        succeeded.Length.Should().Be(finalSlot.BookedCount,
            "回報給呼叫端的成功數必須與資料庫實際賣出的數量一致");

        activeAppointments.Select(a => a.PatientId).Should().BeEquivalentTo(succeeded.Select(r => r.PatientId));
        activeAppointments.Should().OnlyContain(a => a.SlotId == slotId);
    }

    /// <summary>
    /// Arrange 刻意不走 DatabaseSeeder：Seeder 的職責是「讓本機 dotnet run 有東西可以手動點」，
    /// 產生的是固定的 2 個醫生 × 3 個時段 + 1 個測試病人；這個測試需要的是「恰好一個
    /// Capacity=5 的時段 + 恰好 50 個不同病人」。兩者混用會讓斷言的數字失去意義，也會讓測試
    /// 被 Seeder 的無關改動弄壞。
    /// </summary>
    private async Task<(long SlotId, long[] PatientIds)> SeedSlotAndPatientsAsync()
    {
        await using var setupScope = _fixture.ScopeFactory.CreateAsyncScope();
        var db = setupScope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        var now = setupScope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var doctor = new Doctor("Concurrency Test Doctor", "Cardiology", now);
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();

        // 時段必須在未來：ScheduleSlot.Book() 對已開始的時段會丟 SlotInPastException，
        // 那是另一條失敗路徑，會污染這個測試要測的東西。
        var start = now.UtcDateTime.AddDays(1);
        var slot = new ScheduleSlot(doctor.Id, new TimeSlot(start, start.AddMinutes(30)), SlotCapacity, now);
        db.ScheduleSlots.Add(slot);
        await db.SaveChangesAsync();

        // Patient 有 FK 指向 User（fk_patients_user），所以 50 個病人要先有 50 個 User。
        var users = Enumerable.Range(1, ConcurrentPatients)
            .Select(i => new User($"patient-{i:D3}@concurrency.test", "not-a-real-password-hash", UserRole.Patient, now))
            .ToArray();
        db.Users.AddRange(users);
        await db.SaveChangesAsync();

        var patients = users.Select((u, i) => new Patient(u.Id, $"Patient {i + 1:D3}", now)).ToArray();
        db.Patients.AddRange(patients);
        await db.SaveChangesAsync();

        return (slot.Id, patients.Select(p => p.Id).ToArray());
    }
}
