using FluentAssertions;
using MedConnect.Application.Appointments.Commands.BookAppointment;
using MedConnect.Application.Common.Validation;
using Xunit;

namespace MedConnect.Application.UnitTests.Appointments.Commands.BookAppointment;

public class BookAppointmentCommandValidatorTests
{
    private readonly BookAppointmentCommandValidator _validator = new();

    [Fact]
    public void Validate_WithPositiveSlotId_ReturnsNoErrors()
    {
        _validator.Validate(new BookAppointmentCommand(PatientId: 7, ScheduleSlotId: 1)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void Validate_WithNonPositiveSlotId_ReturnsErrorNamedAfterTheWireField(long slotId)
    {
        _validator.Validate(new BookAppointmentCommand(7, slotId)).Should()
            .ContainSingle().Which.Should().Be(new ValidationError("slotId", "must be greater than 0"));
    }

    [Fact]
    public void Validate_DoesNotValidatePatientId_BecauseItComesFromTheJwtNotFromUserInput()
    {
        _validator.Validate(new BookAppointmentCommand(PatientId: 0, ScheduleSlotId: 1)).Should().BeEmpty();
    }
}
