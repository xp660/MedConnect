using FluentAssertions;
using MedConnect.Application.Abstractions;
using MedConnect.Application.Appointments.Commands.CancelAppointment;
using MedConnect.Application.Common.Exceptions;
using MedConnect.Domain.Entities;
using MedConnect.Domain.Enums;
using MedConnect.Domain.Exceptions;
using MedConnect.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MedConnect.Application.UnitTests.Appointments.Commands.CancelAppointment;

/// <summary>
/// architecture-plan.md §8.3：只測編排，不重測 Appointment.Cancel()/ScheduleSlot.Release() 本身的規則。
/// </summary>
public class CancelAppointmentHandlerTests
{
    private const long OwnerPatientId = 7;
    private const long OtherPatientId = 8;
    private const long AppointmentId = 5;
    private const long SlotId = 10;

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IAppointmentRepository _appointmentRepository = Substitute.For<IAppointmentRepository>();
    private readonly IScheduleSlotRepository _scheduleSlotRepository = Substitute.For<IScheduleSlotRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _timeProvider = new(Now);

    public CancelAppointmentHandlerTests()
    {
        // ExecuteInTransactionAsync 的替身必須真的去執行傳進來的委派，否則 handler 主體不會跑。
        _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<CancelAppointmentResult>>>(),
                Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<CancellationToken, Task<CancelAppointmentResult>>>()(ci.Arg<CancellationToken>()));
    }

    private CancelAppointmentHandler CreateHandler() =>
        new(_appointmentRepository, _scheduleSlotRepository, _unitOfWork, _timeProvider);

    private static ScheduleSlot CreateSlotWithBookedCount(int bookedCount, int capacity = 3)
    {
        var start = Now.UtcDateTime.AddHours(1);
        var slot = new ScheduleSlot(doctorId: 1, new TimeSlot(start, start.AddMinutes(30)), capacity, Now);
        for (var i = 0; i < bookedCount; i++)
        {
            slot.Book(Now);
        }

        return slot;
    }

    private static Appointment CreateAppointment(long patientId = OwnerPatientId) =>
        new(SlotId, patientId, Now);

    private void GivenAppointment(Appointment? appointment) =>
        _appointmentRepository.GetByIdAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(appointment);

    private void GivenSlot(ScheduleSlot? slot) =>
        _scheduleSlotRepository.GetByIdAsync(SlotId, Arg.Any<CancellationToken>()).Returns(slot);

    private async Task AssertNothingWasWrittenAsync()
    {
        _scheduleSlotRepository.DidNotReceive().Update(Arg.Any<ScheduleSlot>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().ExecuteInTransactionAsync(
            Arg.Any<Func<CancellationToken, Task<CancelAppointmentResult>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenOwnerCancelsBookedAppointment_ReleasesSlotCancelsAppointmentAndSavesTwiceInOneTransaction()
    {
        var slot = CreateSlotWithBookedCount(2);
        var appointment = CreateAppointment();
        GivenAppointment(appointment);
        GivenSlot(slot);

        var result = await CreateHandler().Handle(new CancelAppointmentCommand(AppointmentId, OwnerPatientId), CancellationToken.None);

        slot.BookedCount.Should().Be(1);
        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        _scheduleSlotRepository.Received(1).Update(slot);
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).ExecuteInTransactionAsync(
            Arg.Any<Func<CancellationToken, Task<CancelAppointmentResult>>>(), Arg.Any<CancellationToken>());
        result.Status.Should().Be(AppointmentStatus.Cancelled);
        result.CancelledAtUtc.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task Handle_FlushesSlotBeforeAppointment_SameLockOrderAsBooking()
    {
        // 這條測的是 Handler 裡最容易被「整理」掉的順序：兩次 SaveChanges 的當下，實體處於什麼狀態。
        // 第一次 flush 時 slot 必須已經 Release、appointment 還必須是 Booked（否則 appointment 的 UPDATE
        // 會被併進第一次 flush，EF 又會把 appointments 排在 schedule_slots 前面，鎖定順序就反了）；
        // 第二次 flush 時 appointment 才是 Cancelled。
        var slot = CreateSlotWithBookedCount(1);
        var appointment = CreateAppointment();
        GivenAppointment(appointment);
        GivenSlot(slot);

        var snapshots = new List<(int SlotBookedCount, AppointmentStatus AppointmentStatus)>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                snapshots.Add((slot.BookedCount, appointment.Status));
                return Task.CompletedTask;
            });

        await CreateHandler().Handle(new CancelAppointmentCommand(AppointmentId, OwnerPatientId), CancellationToken.None);

        snapshots.Should().Equal(
            (0, AppointmentStatus.Booked),
            (0, AppointmentStatus.Cancelled));
    }

    [Fact]
    public async Task Handle_WhenAppointmentDoesNotExist_ThrowsNotFoundAndWritesNothing()
    {
        GivenAppointment(null);

        var act = async () => await CreateHandler().Handle(new CancelAppointmentCommand(AppointmentId, OwnerPatientId), CancellationToken.None);

        await act.Should().ThrowAsync<AppointmentNotFoundException>().WithMessage("Appointment 5 was not found.");
        await AssertNothingWasWrittenAsync();
    }

    [Fact]
    public async Task Handle_WhenAppointmentBelongsToSomeoneElse_ThrowsExactlyTheSameExceptionAsNotFound()
    {
        // 防 Enumeration：型別與訊息都必須跟「查無此預約」完全一致，呼叫端無法分辨是哪一種。
        var appointment = CreateAppointment(patientId: OtherPatientId);
        GivenAppointment(appointment);
        GivenSlot(CreateSlotWithBookedCount(1));

        var act = async () => await CreateHandler().Handle(new CancelAppointmentCommand(AppointmentId, OwnerPatientId), CancellationToken.None);

        var thrown = await act.Should().ThrowExactlyAsync<AppointmentNotFoundException>();
        thrown.Which.Message.Should().Be("Appointment 5 was not found.");
        appointment.Status.Should().Be(AppointmentStatus.Booked, "別人的預約不可被動到");
        await AssertNothingWasWrittenAsync();
    }

    [Fact]
    public async Task Handle_WhenSomeoneElsesAppointmentIsAlreadyCancelled_StillThrowsNotFoundNotAlreadyCancelled()
    {
        // 所有權檢查必須排在「已取消」檢查之前：否則陌生人可以從 409 vs 404 的差異，
        // 推測出別人預約的取消狀態。
        var appointment = CreateAppointment(patientId: OtherPatientId);
        appointment.Cancel(Now);
        GivenAppointment(appointment);
        GivenSlot(CreateSlotWithBookedCount(0));

        var act = async () => await CreateHandler().Handle(new CancelAppointmentCommand(AppointmentId, OwnerPatientId), CancellationToken.None);

        await act.Should().ThrowExactlyAsync<AppointmentNotFoundException>();
        await AssertNothingWasWrittenAsync();
    }

    [Fact]
    public async Task Handle_WhenAppointmentIsAlreadyCancelled_ThrowsAlreadyCancelledAndDoesNotTouchSlot()
    {
        var slot = CreateSlotWithBookedCount(2);
        var appointment = CreateAppointment();
        appointment.Cancel(Now);
        GivenAppointment(appointment);
        GivenSlot(slot);

        var act = async () => await CreateHandler().Handle(new CancelAppointmentCommand(AppointmentId, OwnerPatientId), CancellationToken.None);

        await act.Should().ThrowAsync<AppointmentAlreadyCancelledException>();
        slot.BookedCount.Should().Be(2, "重複取消不可以再釋放一次名額（double-release）");
        await AssertNothingWasWrittenAsync();
    }

    [Fact]
    public async Task Handle_WhenAlreadyCancelledAndSlotBookedCountIsZero_ThrowsAlreadyCancelledNotReleaseUnderflow()
    {
        // 這是「必須在 Release() 之前先驗證已取消」的回歸測試：時段上唯一的預約被取消後 BookedCount
        // 是 0，若先 Release() 就會丟 SlotReleaseUnderflowException（未對映 → 500），而不是 409。
        var slot = CreateSlotWithBookedCount(0);
        var appointment = CreateAppointment();
        appointment.Cancel(Now);
        GivenAppointment(appointment);
        GivenSlot(slot);

        var act = async () => await CreateHandler().Handle(new CancelAppointmentCommand(AppointmentId, OwnerPatientId), CancellationToken.None);

        await act.Should().ThrowExactlyAsync<AppointmentAlreadyCancelledException>();
        await AssertNothingWasWrittenAsync();
    }

    [Fact]
    public async Task Handle_WhenSlotIsMissingForExistingAppointment_ThrowsInvalidOperationRatherThanSlotNotFound()
    {
        // 資料不一致的 bug，不是呼叫端可處理的「找不到」，刻意不對映成 404 SLOT_NOT_FOUND。
        GivenAppointment(CreateAppointment());
        GivenSlot(null);

        var act = async () => await CreateHandler().Handle(new CancelAppointmentCommand(AppointmentId, OwnerPatientId), CancellationToken.None);

        await act.Should().ThrowExactlyAsync<InvalidOperationException>();
        await AssertNothingWasWrittenAsync();
    }
}
