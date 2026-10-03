using FluentAssertions;
using MediatR;
using MedConnect.Application;
using MedConnect.Application.Abstractions;
using MedConnect.Application.Appointments.Commands.BookAppointment;
using MedConnect.Application.Appointments.Commands.CancelAppointment;
using MedConnect.Application.Common.Behaviors;
using MedConnect.Application.Common.Exceptions;
using MedConnect.Application.Common.Validation;
using MedConnect.Domain.Entities;
using MedConnect.Domain.Enums;
using MedConnect.Domain.Exceptions;
using MedConnect.Domain.ValueObjects;
using MedConnect.Infrastructure;
using MedConnect.Infrastructure.Persistence;
using MedConnect.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedConnect.IntegrationTests.Appointments;

/// <summary>
/// 端到端驗證 RetryBehavior 包著真實 Handler、真實 MySQL、真實的共用 Scoped DbContext（單一 scope 內重試）。
///
/// 與 ConcurrentBookingDistributionTests 的分工：那邊量測「50 人搶 5 個」的成功數分佈；這邊用
/// RetryProbe 確定性地讓第一次嘗試失敗，驗證「重試的每一個機制細節」——特別是 ResetTracking：
/// 拿掉 RetryBehavior 裡那行 ResetTracking()，這裡的 Booking 與 Cancel 測試必須變紅（mutation 驗證）。
/// </summary>
[Collection(MySqlContainerCollection.Name)]
public class RetryPipelineTests
{
    private readonly MySqlContainerFixture _fixture;

    public RetryPipelineTests(MySqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Booking_that_loses_a_race_retries_in_the_same_scope_and_succeeds_on_fresh_data()
    {
        var (slotId, patientIds) = await SeedAsync(capacity: 5, patientCount: 2);
        var (victim, competitor) = (patientIds[0], patientIds[1]);

        await using var harness = await Harness.CreateAsync(_fixture);
        harness.Probe.BeforeFirstTransaction = () => harness.SendInNewScopeAsync(new BookAppointmentCommand(competitor, slotId));

        // 單一 scope：RetryBehavior 與 Handler 共用同一個 DbContext，正是風險所在。
        await using var scope = harness.Provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new BookAppointmentCommand(victim, slotId));

        result.PatientId.Should().Be(victim);
        harness.Probe.ResetTrackingCalls.Should().Be(1, "第一次因版本衝突失敗，重試一次後成功");

        var state = await ReadStateAsync(slotId);
        state.Slot.BookedCount.Should().Be(2, "競爭者 1 + 重試成功的 victim 1");
        state.Slot.Version.Should().Be(2, "兩次成功的 UPDATE，失敗那次已完全回滾");
        state.ActiveAppointments.Should().Be(2);
    }

    [Fact]
    public async Task Booking_retry_that_finds_the_slot_full_ends_in_SLOT_FULL_not_RETRY_EXHAUSTED()
    {
        var (slotId, patientIds) = await SeedAsync(capacity: 1, patientCount: 2);
        var (victim, competitor) = (patientIds[0], patientIds[1]);

        await using var harness = await Harness.CreateAsync(_fixture);
        harness.Probe.BeforeFirstTransaction = () => harness.SendInNewScopeAsync(new BookAppointmentCommand(competitor, slotId));

        await using var scope = harness.Provider.CreateAsyncScope();
        var act = async () => await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new BookAppointmentCommand(victim, slotId));

        // 重試後重讀到「已滿」，是業務上已確定的結果：直接以 SlotFullException 結束，不再重試、也不是耗盡。
        await act.Should().ThrowExactlyAsync<SlotFullException>();
        harness.Probe.ResetTrackingCalls.Should().Be(1, "只重試了那一次，看到已滿就停");

        var state = await ReadStateAsync(slotId);
        state.Slot.BookedCount.Should().Be(1);
        state.ActiveAppointments.Should().Be(1);
    }

    [Fact]
    public async Task Cancel_whose_second_flush_conflicts_rolls_back_the_first_flush_and_the_retry_releases_the_slot_exactly_once()
    {
        // 3 個病人各訂一筆（booked_count=3）：若 retry 造成 double-release，數字會變成 1 或更低。
        var (slotId, patientIds) = await SeedAsync(capacity: 5, patientCount: 3);
        await using var harness = await Harness.CreateAsync(_fixture);
        long targetAppointmentId = 0;
        foreach (var patientId in patientIds)
        {
            var booked = await harness.SendInNewScopeAsync(new BookAppointmentCommand(patientId, slotId));
            if (patientId == patientIds[0])
            {
                targetAppointmentId = booked.AppointmentId;
            }
        }

        var before = await ReadStateAsync(slotId);
        before.Slot.BookedCount.Should().Be(3);
        before.Slot.Version.Should().Be(3);

        // 競爭者只動 appointments 的 version（不動 slot）：victim 的第一次 flush（schedule_slots UPDATE）會成功，
        // 第二次 flush（appointments UPDATE）才衝突——正是「雙階段 flush」最危險的視窗：
        // 交易回滾了，但記憶體中的 slot 已被 Release()、version 已被推進。
        harness.Probe.BeforeFirstTransaction = async () =>
        {
            await using var otherScope = harness.Provider.CreateAsyncScope();
            var db = otherScope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE appointments SET version = version + 1 WHERE id = {0}", targetAppointmentId);
        };

        await using var scope = harness.Provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new CancelAppointmentCommand(targetAppointmentId, patientIds[0]));

        result.Status.Should().Be(AppointmentStatus.Cancelled);
        harness.Probe.ResetTrackingCalls.Should().Be(1);

        var after = await ReadStateAsync(slotId);
        after.Slot.BookedCount.Should().Be(2, "3 筆取消 1 筆：名額只釋放一次，不可 double-release");
        after.Slot.Version.Should().Be(4, "失敗那次嘗試的 slot UPDATE 已隨交易回滾，只留下重試成功的那一次");
        after.ActiveAppointments.Should().Be(after.Slot.BookedCount, "兩表不可 drift");
        after.AppointmentVersion(targetAppointmentId).Should().Be(2, "競爭者 +1，重試成功的 Cancel +1");
    }

    [Fact]
    public async Task An_invalid_request_fails_validation_once_and_never_reaches_the_retry_loop_or_the_handler()
    {
        await using var harness = await Harness.CreateAsync(_fixture, services =>
            services.AddTransient<IRequestValidator<BookAppointmentCommand>>(_ => Counting));
        Counting.Reset();

        await using var scope = harness.Provider.CreateAsyncScope();
        var act = async () => await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new BookAppointmentCommand(PatientId: 1, ScheduleSlotId: 0)); // slotId > 0 規則不成立

        await act.Should().ThrowExactlyAsync<RequestValidationException>();
        Counting.Calls.Should().Be(1, "驗證只跑一次，沒有被重試迴圈重複執行");
        harness.Probe.TransactionCalls.Should().Be(0, "Handler 完全沒被呼叫，沒開交易");
        harness.Probe.ResetTrackingCalls.Should().Be(0, "沒進入重試迴圈");
    }

    private static readonly CountingValidator Counting = new();

    private sealed class CountingValidator : IRequestValidator<BookAppointmentCommand>
    {
        private int _calls;
        public int Calls => _calls;
        public void Reset() => _calls = 0;

        public IReadOnlyList<ValidationError> Validate(BookAppointmentCommand request)
        {
            Interlocked.Increment(ref _calls);
            return [];
        }
    }

    // ---------- harness ----------

    /// <summary>
    /// 與 MySqlContainerFixture 同一條註冊路徑（AddApplication + AddInfrastructure），差別只有兩處：
    /// RetryOptions 延遲設為 0（測試不需要真的等），以及 IUnitOfWork 被 SabotagingUnitOfWork 包起來。
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required RetryProbe Probe { get; init; }

        public static async Task<Harness> CreateAsync(MySqlContainerFixture fixture, Action<IServiceCollection>? configure = null)
        {
            string connectionString;
            await using (var scope = fixture.ScopeFactory.CreateAsyncScope())
            {
                connectionString = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>().Database.GetConnectionString()!;
            }

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MedConnect"] = connectionString })
                .Build();

            var probe = new RetryProbe();
            var services = new ServiceCollection();
            services.AddSingleton(new RetryOptions { BaseDelay = TimeSpan.Zero, JitterRange = TimeSpan.Zero });
            services.AddApplication();
            services.AddInfrastructure(configuration);

            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<UnitOfWork>();
            services.AddSingleton(probe);
            services.AddScoped<IUnitOfWork>(sp =>
                new SabotagingUnitOfWork(sp.GetRequiredService<UnitOfWork>(), sp.GetRequiredService<RetryProbe>()));

            configure?.Invoke(services);

            return new Harness { Provider = services.BuildServiceProvider(), Probe = probe };
        }

        public async Task<TResponse> SendInNewScopeAsync<TResponse>(IRequest<TResponse> request)
        {
            await using var scope = Provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
        }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    // ---------- helpers ----------

    private sealed record SlotState(ScheduleSlot Slot, int ActiveAppointments, Dictionary<long, int> AppointmentVersions)
    {
        public int AppointmentVersion(long id) => AppointmentVersions[id];
    }

    /// <summary>用全新的 scope 讀（§8.5：避免 REPEATABLE READ 快照）。</summary>
    private async Task<SlotState> ReadStateAsync(long slotId)
    {
        await using var scope = _fixture.ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        var slot = await db.ScheduleSlots.AsNoTracking().SingleAsync(s => s.Id == slotId);
        var appointments = await db.Appointments.AsNoTracking().Where(a => a.SlotId == slotId).ToListAsync();
        return new SlotState(
            slot,
            appointments.Count(a => a.Status == AppointmentStatus.Booked),
            appointments.ToDictionary(a => a.Id, a => a.Version));
    }

    private async Task<(long SlotId, long[] PatientIds)> SeedAsync(int capacity, int patientCount)
    {
        await using var scope = _fixture.ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var doctor = new Doctor("Retry Pipeline Doctor", "Cardiology", now);
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();

        var start = now.UtcDateTime.AddDays(1);
        var slot = new ScheduleSlot(doctor.Id, new TimeSlot(start, start.AddMinutes(30)), capacity, now);
        db.ScheduleSlots.Add(slot);
        await db.SaveChangesAsync();

        var tag = Guid.NewGuid().ToString("N")[..12];
        var users = Enumerable.Range(1, patientCount)
            .Select(i => new User($"{tag}-{i}@retry-pipeline.test", "not-a-real-password-hash", UserRole.Patient, now))
            .ToArray();
        db.Users.AddRange(users);
        await db.SaveChangesAsync();

        var patients = users.Select((u, i) => new Patient(u.Id, $"Pipeline Patient {i + 1}", now)).ToArray();
        db.Patients.AddRange(patients);
        await db.SaveChangesAsync();

        return (slot.Id, patients.Select(p => p.Id).ToArray());
    }
}
