using FluentAssertions;
using MedConnect.Domain.Entities;
using MedConnect.Domain.Enums;
using MedConnect.Domain.Exceptions;
using MedConnect.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MedConnect.Domain.UnitTests.Entities;

public class ScheduleSlotTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ScheduleSlot CreateOpenSlot(int capacity = 2, TimeSpan? startsIn = null)
    {
        var start = Now.UtcDateTime + (startsIn ?? TimeSpan.FromHours(1));
        var timeSlot = new TimeSlot(start, start.AddMinutes(30));
        return new ScheduleSlot(doctorId: 1, timeSlot, capacity, Now);
    }

    [Fact]
    public void Constructor_WithValidCapacity_StartsOpenWithZeroBookedCount()
    {
        var slot = CreateOpenSlot(capacity: 3);

        slot.Status.Should().Be(SlotStatus.Open);
        slot.Capacity.Should().Be(3);
        slot.BookedCount.Should().Be(0);
        slot.AvailableCount.Should().Be(3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithCapacityLessThanOne_Throws(int capacity)
    {
        var start = Now.UtcDateTime.AddHours(1);
        var timeSlot = new TimeSlot(start, start.AddMinutes(30));

        var act = () => new ScheduleSlot(doctorId: 1, timeSlot, capacity, Now);

        act.Should().Throw<InvalidCapacityException>();
    }

    [Fact]
    public void Book_WhenOpenAndHasCapacity_IncrementsBookedCountByExactlyOne()
    {
        var slot = CreateOpenSlot(capacity: 2);

        slot.Book(Now);

        slot.BookedCount.Should().Be(1);
        slot.AvailableCount.Should().Be(1);
    }

    [Fact]
    public void Book_WhenBookedCountReachesCapacity_NextBookThrowsSlotFullException()
    {
        var slot = CreateOpenSlot(capacity: 1);
        slot.Book(Now);

        var act = () => slot.Book(Now);

        act.Should().Throw<SlotFullException>();
        slot.BookedCount.Should().Be(1);
    }

    [Fact]
    public void Book_WhenSlotIsClosed_ThrowsSlotClosedException()
    {
        var slot = CreateOpenSlot();
        slot.Close(Now);

        var act = () => slot.Book(Now);

        act.Should().Throw<SlotClosedException>();
    }

    [Fact]
    public void Close_SetsStatusToClosed()
    {
        var slot = CreateOpenSlot();

        slot.Close(Now);

        slot.Status.Should().Be(SlotStatus.Closed);
    }

    [Fact]
    public void Book_WhenStartTimeHasAlreadyPassed_ThrowsSlotInPastException()
    {
        var slot = CreateOpenSlot(startsIn: TimeSpan.FromHours(-1));

        var act = () => slot.Book(Now);

        act.Should().Throw<SlotInPastException>();
    }

    [Fact]
    public void Book_WhenStartTimeEqualsNow_ThrowsSlotInPastException()
    {
        var slot = CreateOpenSlot(startsIn: TimeSpan.Zero);

        var act = () => slot.Book(Now);

        act.Should().Throw<SlotInPastException>();
    }

    [Fact]
    public void Release_WhenBookedCountIsPositive_DecrementsBookedCountByExactlyOne()
    {
        var slot = CreateOpenSlot(capacity: 2);
        slot.Book(Now);
        slot.Book(Now);

        slot.Release(Now);

        slot.BookedCount.Should().Be(1);
    }

    [Fact]
    public void Release_WhenBookedCountIsZero_ThrowsSlotReleaseUnderflowException()
    {
        var slot = CreateOpenSlot();

        var act = () => slot.Release(Now);

        act.Should().Throw<SlotReleaseUnderflowException>();
    }

    [Fact]
    public void Constructor_SetsUpdatedAtUtcEqualToCreatedAtUtc()
    {
        var slot = CreateOpenSlot();

        slot.UpdatedAtUtc.Should().Be(slot.CreatedAtUtc);
        slot.UpdatedAtUtc.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public void Book_WhenSuccessful_SetsUpdatedAtUtcToGivenNow()
    {
        var slot = CreateOpenSlot();
        var bookedAt = Now.AddMinutes(5);

        slot.Book(bookedAt);

        slot.UpdatedAtUtc.Should().Be(bookedAt.UtcDateTime);
        slot.CreatedAtUtc.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public void Book_WhenItThrows_DoesNotChangeUpdatedAtUtc()
    {
        var slot = CreateOpenSlot();
        slot.Close(Now);
        var attemptedAt = Now.AddMinutes(5);

        var act = () => slot.Book(attemptedAt);

        act.Should().Throw<SlotClosedException>();
        slot.UpdatedAtUtc.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public void Release_WhenSuccessful_SetsUpdatedAtUtcToGivenNow()
    {
        var slot = CreateOpenSlot();
        slot.Book(Now);
        var releasedAt = Now.AddMinutes(5);

        slot.Release(releasedAt);

        slot.UpdatedAtUtc.Should().Be(releasedAt.UtcDateTime);
    }

    [Fact]
    public void Close_SetsUpdatedAtUtcToGivenNow()
    {
        var slot = CreateOpenSlot();
        var closedAt = Now.AddMinutes(5);

        slot.Close(closedAt);

        slot.UpdatedAtUtc.Should().Be(closedAt.UtcDateTime);
    }

    [Fact]
    public void FakeTimeProvider_GetUtcNow_CanDriveDomainMethodsWithoutRealClock()
    {
        var timeProvider = new FakeTimeProvider(Now);
        var slot = CreateOpenSlot(startsIn: TimeSpan.FromMinutes(1));

        slot.Book(timeProvider.GetUtcNow());

        slot.BookedCount.Should().Be(1);
    }
}
