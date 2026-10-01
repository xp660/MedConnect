using FluentAssertions;
using MedConnect.Application.Abstractions;
using MedConnect.Application.Common.Exceptions;
using MedConnect.Application.ScheduleSlots.Queries.GetAvailableSlots;
using NSubstitute;
using Xunit;

namespace MedConnect.Application.UnitTests.ScheduleSlots.Queries.GetAvailableSlots;

public class GetAvailableSlotsHandlerTests
{
    private static readonly DateOnly Date = new(2026, 10, 5);

    private readonly IDoctorRepository _doctorRepository = Substitute.For<IDoctorRepository>();
    private readonly IScheduleSlotQueryRepository _scheduleSlotQueryRepository = Substitute.For<IScheduleSlotQueryRepository>();

    private GetAvailableSlotsHandler CreateHandler() => new(_doctorRepository, _scheduleSlotQueryRepository);

    [Fact]
    public async Task Handle_WhenDoctorDoesNotExist_ThrowsDoctorNotFoundException()
    {
        _doctorRepository.ExistsAsync(404, Arg.Any<CancellationToken>()).Returns(false);
        var query = new GetAvailableSlotsQuery(404, Date);

        var act = async () => await CreateHandler().Handle(query, CancellationToken.None);

        await act.Should().ThrowAsync<DoctorNotFoundException>();
        // 醫生不存在就該直接拒絕，不該再去查時段——查了也只會是空結果，
        // 卻會讓呼叫端把「醫生打錯」誤判成「這天沒診次」。
        await _scheduleSlotQueryRepository.DidNotReceive()
            .GetAvailableSlotsAsync(Arg.Any<long>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenDoctorExistsButHasNoSlotsForDate_ReturnsEmptyList()
    {
        _doctorRepository.ExistsAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        _scheduleSlotQueryRepository.GetAvailableSlotsAsync(1, Date, Arg.Any<CancellationToken>())
            .Returns(new List<ScheduleSlotDto>());
        var query = new GetAvailableSlotsQuery(1, Date);

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenSomeSlotsArePartiallyBooked_ReturnsThemWithTheirAvailableCount()
    {
        var partiallyBooked = new ScheduleSlotDto(1, 1, DateTimeOffset.UtcNow, Capacity: 5, AvailableCount: 3, Status: "Open");
        _doctorRepository.ExistsAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        _scheduleSlotQueryRepository.GetAvailableSlotsAsync(1, Date, Arg.Any<CancellationToken>())
            .Returns(new List<ScheduleSlotDto> { partiallyBooked });
        var query = new GetAvailableSlotsQuery(1, Date);

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.Should().ContainSingle().Which.Should().Be(partiallyBooked);
    }

    [Fact]
    public async Task Handle_WhenASlotIsFullyBooked_StillReturnsItRatherThanFilteringItOut()
    {
        // 已滿的診次（AvailableCount == 0）也要回傳，讓前端自己決定怎麼呈現，
        // Handler 不可以因為「沒名額了」就偷偷把它濾掉。
        var fullyBooked = new ScheduleSlotDto(2, 1, DateTimeOffset.UtcNow, Capacity: 3, AvailableCount: 0, Status: "Open");
        _doctorRepository.ExistsAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        _scheduleSlotQueryRepository.GetAvailableSlotsAsync(1, Date, Arg.Any<CancellationToken>())
            .Returns(new List<ScheduleSlotDto> { fullyBooked });
        var query = new GetAvailableSlotsQuery(1, Date);

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.Should().ContainSingle(s => s.AvailableCount == 0);
    }
}
