using FluentAssertions;
using MedConnect.Application.Appointments.Commands.CancelAppointment;
using MedConnect.Application.Common.Validation;
using Xunit;

namespace MedConnect.Application.UnitTests.Appointments.Commands.CancelAppointment;

public class CancelAppointmentCommandValidatorTests
{
    private readonly CancelAppointmentCommandValidator _validator = new();

    [Fact]
    public void Validate_WithPositiveAppointmentId_ReturnsNoErrors()
    {
        _validator.Validate(new CancelAppointmentCommand(AppointmentId: 1, PatientId: 7)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validate_WithNonPositiveAppointmentId_ReturnsErrorNamedAfterTheRouteParameter(long appointmentId)
    {
        _validator.Validate(new CancelAppointmentCommand(appointmentId, 7)).Should()
            .ContainSingle().Which.Should().Be(new ValidationError("id", "must be greater than 0"));
    }
}
