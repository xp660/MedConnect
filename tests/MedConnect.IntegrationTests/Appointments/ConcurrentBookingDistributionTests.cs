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
/// 量測用測試：重複跑 20 輪「50 個不同病人搶 Capacity=5」，記錄每輪成功數與失敗原因的分佈。
///
/// 為什麼需要它：ConcurrentBookingTests 只跑一輪、且只斷言不變量，看不出「成功數」的分佈。
/// retry 存在的目的就是把成功數往 Capacity 推（architecture-plan.md §12 第 7 項），
/// 要證明它有效，必須在同一組條件下量測加 retry 前後的分佈，不能只憑單次結果。
///
/// 這裡的斷言只有不變量（不超賣、兩表無 drift、回應與 DB 一致、無非預期例外），
/// 分佈與 RETRY_EXHAUSTED 的次數／比例是量測結果、不是斷言——成功數本質上非確定性，
/// 斷言它等於某個值會 flaky（§0 v1.9 第 9 項）。成功數的下限斷言（>= Capacity - 1）放在
/// ConcurrentBookingTests，那是單輪、可當作回歸閘門的版本；這裡只負責「印出趨勢給人看」。
/// </summary>
[Collection(MySqlContainerCollection.Name)]
public class ConcurrentBookingDistributionTests
{
    private const int Rounds = 20;
    private const int SlotCapacity = 5;
    private const int ConcurrentPatients = 50;

    private readonly MySqlContainerFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ConcurrentBookingDistributionTests(MySqlContainerFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task Measure_success_distribution_of_fifty_patients_booking_one_slot_with_capacity_five_over_twenty_rounds()
    {
        var successesPerRound = new List<int>();
        var failureTotals = new Dictionary<string, int>();
        var roundsWithExhausted = 0;

        for (var round = 1; round <= Rounds; round++)
        {
            var (slotId, patientIds) = await SeedSlotAndPatientsAsync(round);

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var attempts = patientIds
                .Select(patientId => Task.Run(async () =>
                {
                    await gate.Task;

                    await using var scope = _fixture.ScopeFactory.CreateAsyncScope();
                    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

                    // 只接預期內的失敗；其餘例外（含死結）故意不接，讓測試紅燈。
                    try
                    {
                        await mediator.Send(new BookAppointmentCommand(patientId, slotId));
                        return new BookingAttemptResult(patientId, Success: true, ErrorCode: null);
                    }
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

            var succeeded = results.Where(r => r.Success).ToArray();
            var failures = results.Where(r => !r.Success).GroupBy(r => r.ErrorCode!).ToDictionary(g => g.Key, g => g.Count());
            if (failures.ContainsKey("RETRY_EXHAUSTED"))
            {
                roundsWithExhausted++;
            }

            foreach (var (code, count) in failures)
            {
                failureTotals[code] = failureTotals.GetValueOrDefault(code) + count;
            }

            await using var verifyScope = _fixture.ScopeFactory.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
            var finalSlot = await db.ScheduleSlots.AsNoTracking().SingleAsync(s => s.Id == slotId);
            var activeCount = await db.Appointments.AsNoTracking()
                .CountAsync(a => a.SlotId == slotId && a.Status == AppointmentStatus.Booked);

            _output.WriteLine(
                $"round {round,2}: succeeded={succeeded.Length} " +
                $"failures=[{string.Join(", ", failures.OrderBy(f => f.Key).Select(f => $"{f.Key}={f.Value}"))}] " +
                $"db booked_count={finalSlot.BookedCount} active_appointments={activeCount}");

            finalSlot.BookedCount.Should().BeLessThanOrEqualTo(finalSlot.Capacity, $"round {round}: 不可超賣");
            finalSlot.BookedCount.Should().Be(activeCount, $"round {round}: 兩表不可 drift");
            succeeded.Length.Should().Be(finalSlot.BookedCount, $"round {round}: 回應與 DB 必須一致");
            succeeded.Select(r => r.PatientId).Should().OnlyHaveUniqueItems();

            successesPerRound.Add(succeeded.Length);
        }

        var histogram = successesPerRound.GroupBy(s => s).OrderBy(g => g.Key)
            .Select(g => $"{g.Key} 成功 × {g.Count()} 輪");
        _output.WriteLine("---- 成功數分佈（共 " + Rounds + " 輪）----");
        foreach (var line in histogram)
        {
            _output.WriteLine("  " + line);
        }

        _output.WriteLine(
            $"平均成功數={successesPerRound.Average():F2} 最小={successesPerRound.Min()} 最大={successesPerRound.Max()} " +
            $"填滿 Capacity({SlotCapacity}) 的輪數={successesPerRound.Count(s => s == SlotCapacity)}");
        _output.WriteLine("失敗原因總計：" + string.Join(", ", failureTotals.OrderBy(f => f.Key).Select(f => $"{f.Key}={f.Value}")));

        // 健康指標（只印出、不斷言）：趨勢上升代表重試參數或鎖競爭出了問題，供人工檢視。
        var totalRequests = Rounds * ConcurrentPatients;
        var exhausted = failureTotals.GetValueOrDefault("RETRY_EXHAUSTED");
        _output.WriteLine(
            $"[health] RETRY_EXHAUSTED={exhausted}/{totalRequests} ({(double)exhausted / totalRequests:P1})，" +
            $"出現的輪數={roundsWithExhausted}/{Rounds}");
    }

    private async Task<(long SlotId, long[] PatientIds)> SeedSlotAndPatientsAsync(int round)
    {
        await using var scope = _fixture.ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var doctor = new Doctor($"Distribution Doctor {round}", "Cardiology", now);
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();

        var start = now.UtcDateTime.AddDays(1);
        var slot = new ScheduleSlot(doctor.Id, new TimeSlot(start, start.AddMinutes(30)), SlotCapacity, now);
        db.ScheduleSlots.Add(slot);
        await db.SaveChangesAsync();

        // email 帶 round 編號：users.email 有唯一索引，20 輪 × 50 人不可重複。
        var users = Enumerable.Range(1, ConcurrentPatients)
            .Select(i => new User($"r{round:D2}-p{i:D3}@distribution.test", "not-a-real-password-hash", UserRole.Patient, now))
            .ToArray();
        db.Users.AddRange(users);
        await db.SaveChangesAsync();

        var patients = users.Select((u, i) => new Patient(u.Id, $"Patient {round:D2}-{i + 1:D3}", now)).ToArray();
        db.Patients.AddRange(patients);
        await db.SaveChangesAsync();

        return (slot.Id, patients.Select(p => p.Id).ToArray());
    }
}
