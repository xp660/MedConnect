using FluentAssertions;
using MedConnect.Application.Common.Validation;
using MedConnect.Application.ScheduleSlots.Queries.GetAvailableSlots;
using Xunit;

namespace MedConnect.Application.UnitTests.ScheduleSlots.Queries.GetAvailableSlots;

public class GetAvailableSlotsQueryValidatorTests
{
    private static readonly DateOnly SomeDate = new(2026, 10, 5);

    private readonly GetAvailableSlotsQueryValidator _validator = new();

    [Fact]
    public void Validate_WithPositiveDoctorIdAndRealDate_ReturnsNoErrors()
    {
        _validator.Validate(new GetAvailableSlotsQuery(1, SomeDate)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithNonPositiveDoctorId_ReturnsDoctorIdError(long doctorId)
    {
        _validator.Validate(new GetAvailableSlotsQuery(doctorId, SomeDate)).Should()
            .ContainSingle().Which.Should().Be(new ValidationError("doctorId", "must be greater than 0"));
    }

    [Fact]
    public void Validate_WithDefaultDate_TreatsItAsMissing()
    {
        // query string 沒帶 date 時，model binding 給的就是 default(DateOnly)。
        _validator.Validate(new GetAvailableSlotsQuery(1, default)).Should()
            .ContainSingle().Which.Should().Be(new ValidationError("date", "is required"));
    }

    [Fact]
    public void Validate_WithBothInvalid_ReturnsBothErrors()
    {
        _validator.Validate(new GetAvailableSlotsQuery(0, default)).Select(e => e.Field)
            .Should().BeEquivalentTo("doctorId", "date");
    }
}
