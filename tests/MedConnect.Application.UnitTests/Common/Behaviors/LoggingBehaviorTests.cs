using FluentAssertions;
using MediatR;
using MedConnect.Application.Common.Behaviors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MedConnect.Application.UnitTests.Common.Behaviors;

public class LoggingBehaviorTests
{
    public sealed record SensitiveRequest(string Secret) : IRequest<string>;

    private readonly CapturingLogger<LoggingBehavior<SensitiveRequest, string>> _logger = new();
    private readonly FakeTimeProvider _timeProvider = new();

    private LoggingBehavior<SensitiveRequest, string> CreateBehavior() => new(_logger, _timeProvider);

    [Fact]
    public async Task Handle_OnSuccess_LogsOneInformationLineWithRequestNameElapsedTimeAndSuccess()
    {
        RequestHandlerDelegate<string> next = _ =>
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(250));
            return Task.FromResult("ok");
        };

        var result = await CreateBehavior().Handle(new SensitiveRequest("hunter2"), next, CancellationToken.None);

        result.Should().Be("ok");
        var entry = _logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.State["RequestName"].Should().Be(nameof(SensitiveRequest));
        entry.State["ElapsedMs"].Should().Be(250d);
        entry.State["Outcome"].Should().Be("Success");
    }

    [Fact]
    public async Task Handle_OnException_LogsOnlyTheExceptionTypeNameAtInformationAndRethrowsTheSameException()
    {
        var failure = new InvalidOperationException("boom");
        RequestHandlerDelegate<string> next = _ => throw failure;

        var act = () => CreateBehavior().Handle(new SensitiveRequest("hunter2"), next, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
        var entry = _logger.Entries.Should().ContainSingle().Subject;
        // 例外的層級只能在 GlobalExceptionHandler 一處決定：這裡不是 Error，也不帶例外物件，否則同一個例外會被記兩次。
        entry.Level.Should().Be(LogLevel.Information);
        entry.Exception.Should().BeNull();
        entry.State["Outcome"].Should().Be(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task Handle_NeverLogsTheRequestContent()
    {
        await CreateBehavior().Handle(new SensitiveRequest("hunter2"), _ => Task.FromResult("ok"), CancellationToken.None);
        var failing = () => CreateBehavior().Handle(
            new SensitiveRequest("hunter2"), _ => throw new InvalidOperationException("boom"), CancellationToken.None);
        await failing.Should().ThrowAsync<InvalidOperationException>();

        _logger.Entries.Should().HaveCount(2);
        foreach (var entry in _logger.Entries)
        {
            entry.Message.Should().NotContain("hunter2");
            entry.State.Values.Select(v => v?.ToString()).Should().NotContain(v => v != null && v.Contains("hunter2"));
        }
    }
}
