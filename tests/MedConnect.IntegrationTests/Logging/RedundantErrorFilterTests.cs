using FluentAssertions;
using MedConnect.Api.Logging;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace MedConnect.IntegrationTests.Logging;

/// <summary>
/// 過濾規則本身的單元測試（不需要資料庫）：精確到「類別 + 事件 Id + Error 層級」，
/// 不是整個類別，也不是整個層級——過濾太寬就會把真正的錯誤吞掉。
/// </summary>
public class RedundantErrorFilterTests
{
    private static LogEvent Event(LogEventLevel level, string sourceContext, int eventId)
    {
        var properties = new[]
        {
            new LogEventProperty("SourceContext", new ScalarValue(sourceContext)),
            new LogEventProperty("EventId", new StructureValue([new LogEventProperty("Id", new ScalarValue(eventId))])),
        };

        return new LogEvent(DateTimeOffset.UtcNow, level, null, new MessageTemplate("x", []), properties);
    }

    [Theory]
    [InlineData("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", 1)]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command", 20102)]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Transaction", 20205)]
    [InlineData("Microsoft.EntityFrameworkCore.Update", 10000)]
    [InlineData("Microsoft.Extensions.Hosting.Internal.Host", 11)]
    public void The_known_redundant_Error_events_are_excluded(string sourceContext, int eventId) =>
        ApiLogging.IsRedundantErrorEvent(Event(LogEventLevel.Error, sourceContext, eventId)).Should().BeTrue();

    [Theory]
    [InlineData("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", 3)] // 同類別的其他事件（處理器本身失敗）要保留
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command", 20101)] // 同類別、不同事件
    [InlineData("MedConnect.Api.ExceptionHandling.GlobalExceptionHandler", 1)] // 事件 Id 相同、類別不同
    public void Other_events_are_kept(string sourceContext, int eventId) =>
        ApiLogging.IsRedundantErrorEvent(Event(LogEventLevel.Error, sourceContext, eventId)).Should().BeFalse();

    [Fact]
    public void The_same_event_at_a_level_other_than_Error_is_kept() =>
        ApiLogging.IsRedundantErrorEvent(Event(LogEventLevel.Warning, "Microsoft.EntityFrameworkCore.Update", 10000)).Should().BeFalse();

    [Fact]
    public void An_event_without_source_context_or_event_id_is_kept() =>
        ApiLogging.IsRedundantErrorEvent(new LogEvent(
            DateTimeOffset.UtcNow, LogEventLevel.Error, null, new MessageTemplate("x", []), [])).Should().BeFalse();
}
