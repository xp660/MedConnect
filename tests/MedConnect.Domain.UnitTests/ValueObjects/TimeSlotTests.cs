using FluentAssertions;
using MedConnect.Domain.Exceptions;
using MedConnect.Domain.ValueObjects;
using Xunit;

namespace MedConnect.Domain.UnitTests.ValueObjects;

public class TimeSlotTests
{
    [Fact]
    public void Constructor_WhenStartBeforeEnd_SetsStartAndEnd()
    {
        var start = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 1, 9, 30, 0, DateTimeKind.Utc);

        var slot = new TimeSlot(start, end);

        slot.StartUtc.Should().Be(start);
        slot.EndUtc.Should().Be(end);
    }

    [Fact]
    public void Constructor_WhenStartEqualsEnd_Throws()
    {
        var same = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

        var act = () => new TimeSlot(same, same);

        act.Should().Throw<InvalidTimeSlotException>();
    }

    [Fact]
    public void Constructor_WhenStartAfterEnd_Throws()
    {
        var start = new DateTime(2026, 1, 1, 9, 30, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

        var act = () => new TimeSlot(start, end);

        act.Should().Throw<InvalidTimeSlotException>();
    }
}
