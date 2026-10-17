using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MedConnect.Api.ExceptionHandling;
using MedConnect.Application.Abstractions;
using MedConnect.Application.Common.Behaviors;
using MedConnect.Infrastructure.Persistence.Seed;
using MedConnect.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Events;
using Xunit;
using Xunit.Abstractions;

namespace MedConnect.IntegrationTests.Logging;

/// <summary>
/// 走真正的 HTTP pipeline + 真實 MySQL，捕捉「正式 logger 過濾之後」的所有日誌（見 <see cref="CapturingLogSink"/>），
/// 驗證 architecture-plan.md §0 v1.12 的日誌決定：
///   - 敏感資料（密碼、JWT）不出現在任何一行日誌；
///   - 每個例外恰好記一筆、層級正確（500 = Error 帶例外；已對映 = 沒有任何 Error）；
///   - 請求範圍內的日誌帶有與回應 traceId 相同的請求編號。
/// </summary>
[Collection(LoggingCollection.Name)]
public sealed class HttpLoggingTests
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public HttpLoggingTests(ApiWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Fact]
    public async Task Login_never_writes_the_password_or_the_issued_jwt_to_any_log_line()
    {
        var sink = new CapturingLogSink();
        using var factory = _factory.WithLogCapture(sink); // Verbose：任何層級、任何類別都收
        var client = factory.CreateClient();
        const string wrongPassword = "Definitely-Wrong-Password-9!";
        const string email = "test-patient@medconnect.local";

        var failed = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = wrongPassword });
        var succeeded = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = DatabaseSeeder.TestPatientPassword });

        failed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        succeeded.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await succeeded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        var everything = sink.Events.Select(e => e.Everything()).ToList();
        everything.Should().NotBeEmpty("測試必須真的捕捉到日誌，否則下面的「不存在」斷言會空洞地通過");
        everything.Should().Contain(line => line.Contains("RequestName=\"LoginCommand\""), "LoggingBehavior 應該記了 Login 這個請求");
        everything.Should().Contain(line => line.Contains("logged in"), "Login 成功的業務事件應該有記");

        AssertNoLineContains(everything, DatabaseSeeder.TestPatientPassword, "密碼不可出現在日誌");
        AssertNoLineContains(everything, wrongPassword, "嘗試用的密碼也不可出現在日誌");
        AssertNoLineContains(everything, token, "簽發出來的 JWT 不可出現在日誌");
        AssertNoLineContains(everything, email, "email 是個資，不可出現在日誌");
    }

    private static void AssertNoLineContains(IEnumerable<string> lines, string secret, string because)
    {
        var offending = lines.Where(line => line.Contains(secret)).Select(line => line.Length > 400 ? line[..400] + "…" : line).ToList();
        offending.Should().BeEmpty(because + $"（出現在 {offending.Count} 行）");
    }

    [Fact]
    public async Task An_unmapped_exception_is_logged_exactly_once_as_Error_with_its_exception()
    {
        var sink = new CapturingLogSink();
        using var factory = _factory.WithLogCapture(sink, configureServices: services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddSingleton<IUnitOfWork>(new ThrowingUnitOfWork());
        });
        var (client, _) = await PatientClients.CreateAuthenticatedAsync(factory);
        var slotId = await PatientClients.CreateSlotAsync(factory, capacity: 5);
        sink.Clear();

        var response = await client.PostAsJsonAsync("/api/v1/appointments", new { slotId });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var errors = sink.Events.Where(e => e.Level >= LogEventLevel.Error).ToList();
        errors.Should().ContainSingle("每個例外恰好記一次；內建 middleware 那一筆已被過濾，所以這一筆就是唯一的紀錄");
        errors[0].Exception.Should().BeOfType<InvalidOperationException>();
        errors[0].IsFrom(typeof(GlobalExceptionHandler)).Should().BeTrue();
    }

    [Fact]
    public async Task A_mapped_409_produces_no_Error_level_log_at_all_and_exactly_one_Information_from_the_exception_handler()
    {
        var sink = new CapturingLogSink();
        using var factory = _factory.WithLogCapture(sink);
        var (client, _) = await PatientClients.CreateAuthenticatedAsync(factory);
        var slotId = await PatientClients.CreateSlotAsync(factory, capacity: 5);
        (await client.PostAsJsonAsync("/api/v1/appointments", new { slotId })).StatusCode.Should().Be(HttpStatusCode.Created);
        sink.Clear();

        // 同一位病人再訂一次：真實的唯一索引違反。EF Core 在例外被轉譯之前自己會記兩筆 Error
        // （Database.Command 20102、Update 10000），內建 middleware 也會再記一筆——全部都必須被過濾掉。
        var response = await client.PostAsJsonAsync("/api/v1/appointments", new { slotId });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        sink.Events.Where(e => e.Level >= LogEventLevel.Error)
            .Should().BeEmpty("已對映的 409 是預期內的正常事件，不該有任何 Error 層級的日誌");
        var handled = sink.Events.Where(e => e.IsFrom(typeof(GlobalExceptionHandler))).ToList();
        handled.Should().ContainSingle();
        handled[0].Level.Should().Be(LogEventLevel.Information);
        handled[0].Text("ErrorCode").Should().Be("DUPLICATE_BOOKING");
        handled[0].Exception.Should().BeNull("已對映的例外不傳例外物件，沒有堆疊");
    }

    [Fact]
    public async Task Request_scoped_logs_carry_the_same_request_id_as_the_traceId_in_the_response()
    {
        var sink = new CapturingLogSink();
        using var factory = _factory.WithLogCapture(sink);
        var (client, _) = await PatientClients.CreateAuthenticatedAsync(factory);
        var slotId = await PatientClients.CreateSlotAsync(factory, capacity: 5);
        await client.PostAsJsonAsync("/api/v1/appointments", new { slotId });
        sink.Clear();

        var response = await client.PostAsJsonAsync("/api/v1/appointments", new { slotId });

        var traceId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("traceId").GetString();
        traceId.Should().NotBeNullOrEmpty();
        var appLogs = sink.Events
            .Where(e => e.IsFrom(typeof(GlobalExceptionHandler)) || e.IsFromNamespace("MedConnect.Application"))
            .ToList();
        appLogs.Should().NotBeEmpty();
        foreach (var log in appLogs)
        {
            log.Text("RequestId").Should().Be(traceId, $"「{log.RenderMessage()}」必須帶有與回應 traceId 相同的請求編號");
        }

        _output.WriteLine($"response traceId = {traceId}");
        foreach (var log in appLogs)
        {
            _output.WriteLine($"  [{log.Level}] RequestId={log.Text("RequestId")} | {log.RenderMessage()}");
        }
    }

    [Fact]
    public async Task Retry_exhaustion_is_logged_as_one_Warning_after_Debug_retry_lines_and_never_as_Error()
    {
        var sink = new CapturingLogSink();
        var unitOfWork = new AlwaysConflictingUnitOfWork(deadlock: false);
        using var factory = _factory.WithLogCapture(sink, configureServices: services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddSingleton<IUnitOfWork>(unitOfWork);
            services.AddSingleton(new RetryOptions { BaseDelay = TimeSpan.Zero, JitterRange = TimeSpan.Zero });
        });
        var (client, _) = await PatientClients.CreateAuthenticatedAsync(factory);
        var slotId = await PatientClients.CreateSlotAsync(factory, capacity: 5);
        sink.Clear();

        var response = await client.PostAsJsonAsync("/api/v1/appointments", new { slotId });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        sink.Events.Where(e => e.Level >= LogEventLevel.Error).Should().BeEmpty();

        var retries = sink.Events.Where(e => e.IsFromNamespace("MedConnect.Application.Common.Behaviors.RetryBehavior")).ToList();
        retries.Should().HaveCount(3, "MaxRetries = 3，每次重試一行");
        retries.Should().OnlyContain(e => e.Level == LogEventLevel.Debug);

        var warnings = sink.Events.Where(e => e.Level == LogEventLevel.Warning && e.IsFrom(typeof(GlobalExceptionHandler))).ToList();
        warnings.Should().ContainSingle();
        warnings[0].Text("ErrorCode").Should().Be("RETRY_EXHAUSTED");
        warnings[0].Text("Attempts").Should().Be("4");
        warnings[0].Text("LastFailureType").Should().Be("ConcurrencyConflictException");
    }
}
