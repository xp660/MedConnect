using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MedConnect.Api.Logging;
using MedConnect.Application.Abstractions;
using MedConnect.Application.Common.Behaviors;
using MedConnect.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Events;
using Xunit;
using Xunit.Abstractions;

namespace MedConnect.IntegrationTests.Logging;

/// <summary>
/// 用「正式的格式器」格式化真實捕捉到的日誌事件，證明輸出長什麼樣子：
///   - 非 Development：每行一個 JSON，SlotId 這類具名參數是獨立的結構化欄位；
///   - Development：人看的單行文字。
/// 同時把四種代表性的日誌各印一行（預約成功、SLOT_FULL、RETRY_EXHAUSTED、未對映的 500），
/// 附在回報裡讓人看得到層級與欄位。
/// </summary>
[Collection(LoggingCollection.Name)]
public sealed class LogOutputFormatTests
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public LogOutputFormatTests(ApiWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Fact]
    public async Task The_production_format_writes_one_json_object_per_event_with_SlotId_as_its_own_structured_field()
    {
        var sink = new CapturingLogSink();
        using var factory = _factory.WithLogCapture(sink, verbose: false);
        var (client, patientId) = await PatientClients.CreateAuthenticatedAsync(factory);
        var slotId = await PatientClients.CreateSlotAsync(factory, capacity: 5);
        sink.Clear();

        var response = await client.PostAsJsonAsync("/api/v1/appointments", new { slotId });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var booked = sink.Events.Single(e => e.MessageTemplate.Text.StartsWith("Appointment {AppointmentId} booked"));
        var line = Format(booked, "Production").TrimEnd();
        _output.WriteLine(line);

        line.Should().NotContain("\n", "一個事件一行");
        using var json = JsonDocument.Parse(line);
        var root = json.RootElement;
        root.TryGetProperty("@l", out _).Should().BeFalse("compact JSON 省略預設的 Information 層級；其他層級才會帶 @l");
        root.GetProperty("@m").GetString().Should().Contain($"slot {slotId}");
        root.GetProperty("SlotId").GetInt64().Should().Be(slotId, "SlotId 必須是獨立的數值欄位，不是只存在於訊息字串裡");
        root.GetProperty("PatientId").GetInt64().Should().Be(patientId);
        root.GetProperty("AppointmentId").GetInt64().Should().BeGreaterThan(0);
        root.GetProperty("SourceContext").GetString().Should().Be("MedConnect.Application.Appointments.Commands.BookAppointment.BookAppointmentHandler");
        root.TryGetProperty("RequestId", out _).Should().BeTrue("請求範圍內的日誌帶有請求編號");
        root.TryGetProperty("@t", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Print_one_sample_line_for_booking_success_SLOT_FULL_RETRY_EXHAUSTED_and_an_unmapped_500()
    {
        // 1) 預約成功  2) SLOT_FULL（容量 1，第二位病人）
        var sink = new CapturingLogSink();
        using var factory = _factory.WithLogCapture(sink, verbose: false);
        var (first, _) = await PatientClients.CreateAuthenticatedAsync(factory);
        var (second, _) = await PatientClients.CreateAuthenticatedAsync(factory);
        var slotId = await PatientClients.CreateSlotAsync(factory, capacity: 1);
        sink.Clear();
        (await first.PostAsJsonAsync("/api/v1/appointments", new { slotId })).StatusCode.Should().Be(HttpStatusCode.Created);
        var booked = sink.Events.Single(e => e.MessageTemplate.Text.StartsWith("Appointment {AppointmentId} booked"));
        Print("預約成功", booked);

        sink.Clear();
        var full = await second.PostAsJsonAsync("/api/v1/appointments", new { slotId });
        full.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var slotFull = sink.Events.Single(e => e.Text("ErrorCode") == "SLOT_FULL");
        slotFull.Level.Should().Be(LogEventLevel.Information);
        Print("SLOT_FULL", slotFull);

        // 3) RETRY_EXHAUSTED
        var retrySink = new CapturingLogSink();
        using var retryFactory = _factory.WithLogCapture(retrySink, verbose: false, configureServices: services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddSingleton<IUnitOfWork>(new AlwaysConflictingUnitOfWork(deadlock: false));
            services.AddSingleton(new RetryOptions { BaseDelay = TimeSpan.Zero, JitterRange = TimeSpan.Zero });
        });
        var (retryClient, _) = await PatientClients.CreateAuthenticatedAsync(retryFactory);
        var retrySlot = await PatientClients.CreateSlotAsync(retryFactory, capacity: 5);
        retrySink.Clear();
        (await retryClient.PostAsJsonAsync("/api/v1/appointments", new { slotId = retrySlot })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var exhausted = retrySink.Events.Single(e => e.Text("ErrorCode") == "RETRY_EXHAUSTED");
        exhausted.Level.Should().Be(LogEventLevel.Warning);
        Print("RETRY_EXHAUSTED", exhausted);

        // 4) 未對映的 500
        var bugSink = new CapturingLogSink();
        using var bugFactory = _factory.WithLogCapture(bugSink, verbose: false, configureServices: services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddSingleton<IUnitOfWork>(new ThrowingUnitOfWork());
        });
        var (bugClient, _) = await PatientClients.CreateAuthenticatedAsync(bugFactory);
        var bugSlot = await PatientClients.CreateSlotAsync(bugFactory, capacity: 5);
        bugSink.Clear();
        (await bugClient.PostAsJsonAsync("/api/v1/appointments", new { slotId = bugSlot })).StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var bug = bugSink.Events.Single(e => e.Level == LogEventLevel.Error);
        Print("未對映的 500", bug, includeException: true);
    }

    private void Print(string label, LogEvent logEvent, bool includeException = false)
    {
        _output.WriteLine($"--- {label} ---");
        _output.WriteLine("[Development] " + Format(logEvent, "Development").TrimEnd());
        var json = Format(logEvent, "Production").TrimEnd();
        _output.WriteLine("[Production ] " + (includeException ? json : json));
        if (includeException)
        {
            json.Should().Contain("\"@x\"", "未對映的例外必須帶完整例外（堆疊）");
        }
    }

    private static string Format(LogEvent logEvent, string environmentName)
    {
        using var writer = new StringWriter();
        ApiLogging.CreateConsoleFormatter(environmentName).Format(logEvent, writer);

        return writer.ToString();
    }
}
