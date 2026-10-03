using FluentAssertions;
using MediatR;
using MedConnect.Application.Abstractions;
using MedConnect.Application.Common.Behaviors;
using MedConnect.Application.Common.Exceptions;
using MedConnect.Domain.Exceptions;
using NSubstitute;
using Xunit;

namespace MedConnect.Application.UnitTests.Common.Behaviors;

public class RetryBehaviorTests
{
    public sealed record SampleRequest : IRequest<string>;

    // 延遲設成 0：這裡測的是「重試什麼、重試幾次、重試前做了什麼」，不是等待時間。
    private static readonly RetryOptions NoDelay = new() { BaseDelay = TimeSpan.Zero, JitterRange = TimeSpan.Zero };

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private RetryBehavior<SampleRequest, string> CreateBehavior(RetryOptions? options = null) =>
        new(_unitOfWork, options ?? NoDelay, TimeProvider.System);

    private static ConcurrencyConflictException Conflict() => new(new Exception("version mismatch"));

    private static TransientConflictException Deadlock() => new(new Exception("deadlock"));

    /// <summary>前 failuresBeforeSuccess 次丟指定例外，之後回傳 "ok"；並記錄被呼叫幾次。</summary>
    private static (RequestHandlerDelegate<string> Next, Func<int> Calls) FailThenSucceed(
        int failuresBeforeSuccess, Func<Exception> failure)
    {
        var calls = 0;
        RequestHandlerDelegate<string> next = _ =>
        {
            calls++;
            return calls <= failuresBeforeSuccess ? throw failure() : Task.FromResult("ok");
        };
        return (next, () => calls);
    }

    [Fact]
    public async Task Handle_WhenTheFirstAttemptSucceeds_CallsNextOnceAndNeverResetsTracking()
    {
        var (next, calls) = FailThenSucceed(0, Conflict);

        var result = await CreateBehavior().Handle(new SampleRequest(), next, CancellationToken.None);

        result.Should().Be("ok");
        calls().Should().Be(1);
        // 第一次執行不是重試，不該清 tracker（清了只是白白丟掉剛載入的實體）。
        _unitOfWork.DidNotReceive().ResetTracking();
    }

    [Fact]
    public async Task Handle_WhenAConcurrencyConflictClears_RetriesAndReturnsTheResult()
    {
        var (next, calls) = FailThenSucceed(2, Conflict);

        var result = await CreateBehavior().Handle(new SampleRequest(), next, CancellationToken.None);

        result.Should().Be("ok");
        calls().Should().Be(3);
    }

    [Fact]
    public async Task Handle_WhenATransientConflictClears_RetriesAndReturnsTheResult()
    {
        var (next, calls) = FailThenSucceed(1, Deadlock);

        var result = await CreateBehavior().Handle(new SampleRequest(), next, CancellationToken.None);

        result.Should().Be("ok");
        calls().Should().Be(2);
    }

    [Fact]
    public async Task Handle_ResetsTrackingBeforeEveryRetry_ExactlyOncePerRetry()
    {
        var order = new List<string>();
        _unitOfWork.When(u => u.ResetTracking()).Do(_ => order.Add("reset"));
        var calls = 0;
        RequestHandlerDelegate<string> next = _ =>
        {
            order.Add("next");
            return ++calls <= 2 ? throw Conflict() : Task.FromResult("ok");
        };

        await CreateBehavior().Handle(new SampleRequest(), next, CancellationToken.None);

        // 每次重試前都要清 tracker，且一定發生在下一次 next() 之前，否則 Handler 重讀到的還是舊實體。
        order.Should().Equal("next", "reset", "next", "reset", "next");
    }

    [Fact]
    public async Task Handle_WhenEveryAttemptConflicts_ThrowsRetryExhaustedAfterOnePlusMaxRetriesAttempts()
    {
        var (next, calls) = FailThenSucceed(int.MaxValue, Conflict);

        var act = async () => await CreateBehavior().Handle(new SampleRequest(), next, CancellationToken.None);

        var thrown = (await act.Should().ThrowExactlyAsync<RetryExhaustedException>()).Which;
        calls().Should().Be(4, "首次執行 + 3 次重試");
        thrown.Attempts.Should().Be(4);
        thrown.InnerException.Should().BeOfType<ConcurrencyConflictException>();
        _unitOfWork.Received(3).ResetTracking();
    }

    [Fact]
    public async Task Handle_WhenTheLastFailureIsADeadlock_RetryExhaustedKeepsItAsTheInnerException()
    {
        // TRANSIENT_CONFLICT 與 CONCURRENCY_CONFLICT 的區分不能在耗盡時被抹掉：它活在 InnerException。
        var (next, _) = FailThenSucceed(int.MaxValue, Deadlock);

        var act = async () => await CreateBehavior().Handle(new SampleRequest(), next, CancellationToken.None);

        var thrown = (await act.Should().ThrowExactlyAsync<RetryExhaustedException>()).Which;
        thrown.InnerException.Should().BeOfType<TransientConflictException>();
    }

    [Fact]
    public async Task Handle_HonoursMaxRetries()
    {
        var (next, calls) = FailThenSucceed(int.MaxValue, Conflict);
        var behavior = CreateBehavior(new RetryOptions { MaxRetries = 1, BaseDelay = TimeSpan.Zero, JitterRange = TimeSpan.Zero });

        var act = async () => await behavior.Handle(new SampleRequest(), next, CancellationToken.None);

        await act.Should().ThrowExactlyAsync<RetryExhaustedException>();
        calls().Should().Be(2);
    }

    public static TheoryData<Exception> BusinessOutcomes => new()
    {
        new SlotFullException(1),
        new AppointmentAlreadyCancelledException(1),
        new DuplicateBookingException(1, 2, new Exception("dup")),
        new InvalidOperationException("some bug"),
    };

    [Theory]
    [MemberData(nameof(BusinessOutcomes))]
    public async Task Handle_DoesNotRetryBusinessOutcomesOrUnexpectedExceptions(Exception failure)
    {
        var (next, calls) = FailThenSucceed(int.MaxValue, () => failure);

        var act = async () => await CreateBehavior().Handle(new SampleRequest(), next, CancellationToken.None);

        (await act.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(failure, "原樣往外丟，不包裝");
        calls().Should().Be(1, "重試對已確定的結果沒有意義");
        _unitOfWork.DidNotReceive().ResetTracking();
    }

    [Fact]
    public void GetDelay_IsBaseDelayTimesAttemptPlusOne_PlusJitterWithinRange()
    {
        var options = new RetryOptions(); // 預設：25ms、jitter 50ms
        var random = new Random(12345);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var floor = TimeSpan.FromMilliseconds(25 * (attempt + 1));
            for (var i = 0; i < 200; i++)
            {
                var delay = options.GetDelay(attempt, random);
                delay.Should().BeGreaterThanOrEqualTo(floor);
                delay.Should().BeLessThan(floor + TimeSpan.FromMilliseconds(50));
            }
        }
    }

    [Fact]
    public void GetDelay_WithZeroJitter_IsDeterministic()
    {
        var options = new RetryOptions { BaseDelay = TimeSpan.FromMilliseconds(10), JitterRange = TimeSpan.Zero };

        options.GetDelay(0, new Random()).Should().Be(TimeSpan.FromMilliseconds(10));
        options.GetDelay(2, new Random()).Should().Be(TimeSpan.FromMilliseconds(30));
    }
}
