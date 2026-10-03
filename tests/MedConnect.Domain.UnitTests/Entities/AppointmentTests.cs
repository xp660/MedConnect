using FluentAssertions;
using MedConnect.Domain.Entities;
using MedConnect.Domain.Enums;
using MedConnect.Domain.Exceptions;
using Xunit;

namespace MedConnect.Domain.UnitTests.Entities;

public class AppointmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_SetsStatusBookedAndBookedAtUtc()
    {
        var appointment = new Appointment(slotId: 1, patientId: 2, Now);

        appointment.Status.Should().Be(AppointmentStatus.Booked);
        appointment.BookedAtUtc.Should().Be(Now.UtcDateTime);
        appointment.CancelledAtUtc.Should().BeNull();
    }

    [Fact]
    public void Cancel_WhenBooked_SetsStatusCancelledAndCancelledAtUtc()
    {
        var appointment = new Appointment(slotId: 1, patientId: 2, Now);
        var cancelledAt = Now.AddMinutes(10);

        appointment.Cancel(cancelledAt);

        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        appointment.CancelledAtUtc.Should().Be(cancelledAt.UtcDateTime);
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_ThrowsInsteadOfSilentlySucceeding()
    {
        var appointment = new Appointment(slotId: 1, patientId: 2, Now);
        appointment.Cancel(Now);

        var act = () => appointment.Cancel(Now);

        act.Should().Throw<AppointmentAlreadyCancelledException>();
    }

    [Fact]
    public void EnsureCanBeCancelled_WhenBooked_DoesNotThrowAndDoesNotMutate()
    {
        var appointment = new Appointment(slotId: 1, patientId: 2, Now);

        appointment.EnsureCanBeCancelled();

        // 純粹的驗證：不可以順手改狀態，否則 Handler 在 Release() 之前呼叫它就會讓
        // Appointment 提前變 Modified，破壞「slot 先 flush、appointment 後 flush」的順序。
        appointment.Status.Should().Be(AppointmentStatus.Booked);
        appointment.CancelledAtUtc.Should().BeNull();
    }

    [Fact]
    public void EnsureCanBeCancelled_WhenAlreadyCancelled_Throws()
    {
        var appointment = new Appointment(slotId: 1, patientId: 2, Now);
        appointment.Cancel(Now);

        var act = () => appointment.EnsureCanBeCancelled();

        act.Should().Throw<AppointmentAlreadyCancelledException>();
    }
}
