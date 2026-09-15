using FluentAssertions;
using MedConnect.Application.Abstractions;
using MedConnect.Application.Appointments.Commands.BookAppointment;
using MedConnect.Application.Common.Exceptions;
using MedConnect.Domain.Entities;
using MedConnect.Domain.Enums;
using MedConnect.Domain.Exceptions;
using MedConnect.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MedConnect.Application.UnitTests.Appointments.Commands.BookAppointment;

/// <summary>
/// architecture-plan.md §8.3：只測編排（有沒有正確呼叫 Repository/SaveChanges），
/// 不重測 ScheduleSlot.Book() 本身的規則（那些在 MedConnect.Domain.UnitTests 已經覆蓋）。
/// </summary>
public class BookAppointmentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IScheduleSlotRepository _scheduleSlotRepository = Substitute.For<IScheduleSlotRepository>();
    private readonly IAppointmentRepository _appointmentRepository = Substitute.For<IAppointmentRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _timeProvider = new(Now);

    private BookAppointmentHandler CreateHandler() =>
        new(_scheduleSlotRepository, _appointmentRepository, _unitOfWork, _timeProvider);

    private static ScheduleSlot CreateOpenSlot(int capacity = 2)
    {
        var start = Now.UtcDateTime.AddHours(1);
        var timeSlot = new TimeSlot(start, start.AddMinutes(30));
        return new ScheduleSlot(doctorId: 1, timeSlot, capacity, Now);
    }

    [Fact]
    public async Task Handle_WhenSlotExists_BooksSlotAndCreatesAppointmentAndSavesExactlyOnce()
    {
        var slot = CreateOpenSlot(capacity: 2);
        _scheduleSlotRepository.GetByIdAsync(42, Arg.Any<CancellationToken>()).Returns(slot);
        var command = new BookAppointmentCommand(PatientId: 7, ScheduleSlotId: 42);

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        slot.BookedCount.Should().Be(1);
        _scheduleSlotRepository.Received(1).Update(slot);
        _appointmentRepository.Received(1).Add(Arg.Is<Appointment>(a => a.PatientId == 7 && a.SlotId == slot.Id));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        result.PatientId.Should().Be(7);
        result.Status.Should().Be(AppointmentStatus.Booked);
    }

    [Fact]
    public async Task Handle_WhenSlotDoesNotExist_ThrowsAndDoesNotSave()
    {
        _scheduleSlotRepository.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((ScheduleSlot?)null);
        var command = new BookAppointmentCommand(PatientId: 7, ScheduleSlotId: 99);

        var act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ScheduleSlotNotFoundException>();
        _appointmentRepository.DidNotReceive().Add(Arg.Any<Appointment>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenSlotIsFull_PropagatesDomainExceptionAndDoesNotSave()
    {
        var slot = CreateOpenSlot(capacity: 1);
        slot.Book(Now); // 先訂滿唯一的名額，讓下一次 Book() 真的因為滿了而丟例外
        _scheduleSlotRepository.GetByIdAsync(42, Arg.Any<CancellationToken>()).Returns(slot);
        var command = new BookAppointmentCommand(PatientId: 7, ScheduleSlotId: 42);

        var act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<SlotFullException>();
        _appointmentRepository.DidNotReceive().Add(Arg.Any<Appointment>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenUnitOfWorkThrowsConcurrencyConflict_PropagatesWithoutSwallowing()
    {
        var slot = CreateOpenSlot(capacity: 2);
        _scheduleSlotRepository.GetByIdAsync(42, Arg.Any<CancellationToken>()).Returns(slot);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new ConcurrencyConflictException(new Exception("simulated version mismatch"))));
        var command = new BookAppointmentCommand(PatientId: 7, ScheduleSlotId: 42);

        var act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }
}
