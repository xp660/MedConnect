using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MedConnect.Application.Common.Behaviors;
using MedConnect.Infrastructure.Persistence;
using MedConnect.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Serilog.Events;
using Xunit;
using Xunit.Abstractions;

namespace MedConnect.IntegrationTests.Logging;

/// <summary>
/// 真實死結（InnoDB 1213）那一路的日誌行為。死結不是用假的 UnitOfWork 偽造的，而是在真實 MySQL 上用
/// 「兩個交易以相反順序取得兩把鎖」造出來：
///
///   交易 E（測試自己開的連線）：先鎖住病人 P 的那一列。
///   請求 R（HTTP 預約，病人就是 P）：UPDATE schedule_slots（拿到時段 S 的 X 鎖）→ INSERT appointments，
///     外鍵檢查要對 patients(P) 拿 S 鎖 → 被 E 擋住，等待中。
///   E 接著要鎖時段 S → 等 R → 形成環 → InnoDB 偵測到死結，挑一個犧牲者。
///
/// 犧牲者由 InnoDB 依「交易權重」（改動的列數 + 持有的鎖）挑選，權重小的被犧牲。所以 E 先塞 60 列資料把自己
/// 變重，讓 R（只改了 1 列）穩定地成為犧牲者。R 被殺 → UnitOfWork 轉成 TransientConflictException →
/// RetryBehavior 重試；E 在拿到 S 之後立刻 ROLLBACK，重試就順利完成 → 201。
///
/// 要驗證的是：整個過程「沒有任何 Error 層級的日誌」。EF Core 在死結發生時自己會記 Error
/// （Database.Command 20102 / Update 10000，與重複預約同樣的兩個事件），那兩筆必須已被 ApiLogging 過濾。
/// </summary>
[Collection(LoggingCollection.Name)]
public sealed class DeadlockLoggingTests
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public DeadlockLoggingTests(ApiWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Fact]
    public async Task A_real_deadlock_on_the_booking_path_is_retried_and_produces_no_Error_level_log()
    {
        var sink = new CapturingLogSink();
        using var factory = _factory.WithLogCapture(sink); // Verbose：連 Debug 的重試行都要看到
        var (client, patientId) = await PatientClients.CreateAuthenticatedAsync(factory);
        var slotId = await PatientClients.CreateSlotAsync(factory, capacity: 5);
        var rootConnectionString = RootConnectionString(factory);
        sink.Clear();

        await using var blocker = new MySqlConnection(rootConnectionString);
        await blocker.OpenAsync();
        await using var blockerTransaction = await blocker.BeginTransactionAsync();

        // 1) E 先把自己變重，再鎖住病人 P。
        await Execute(blocker, blockerTransaction, string.Join(
            " ",
            "INSERT INTO doctors (full_name, specialty, created_at) VALUES",
            string.Join(",", Enumerable.Range(0, 60).Select(i => $"('deadlock-weight-{Guid.NewGuid():N}-{i}', 'x', UTC_TIMESTAMP(6))"))));
        await Execute(blocker, blockerTransaction, $"SELECT id FROM patients WHERE id = {patientId} FOR UPDATE");

        // 2) R：預約。會先鎖住時段 S，然後在 appointments INSERT 的外鍵檢查被 E 擋住。
        var request = client.PostAsJsonAsync("/api/v1/appointments", new { slotId });
        await WaitUntilSomeTransactionIsWaitingForALockAsync(rootConnectionString);

        // 3) E 要時段 S → 死結。InnoDB 應該挑 R 當犧牲者（E 權重大得多）。
        var blockerGotTheLock = false;
        try
        {
            await Execute(blocker, blockerTransaction, $"SELECT id FROM schedule_slots WHERE id = {slotId} FOR UPDATE");
            blockerGotTheLock = true;
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.LockDeadlock)
        {
            // E 被選為犧牲者：這次沒有造出「R 被殺」的情境，下面的斷言會明確指出。
        }

        await blockerTransaction.RollbackAsync();
        var response = await request;

        blockerGotTheLock.Should().BeTrue("E 應該是贏家（權重大）；若 E 被當成犧牲者，代表死結沒有照設計落在請求上");

        var retryLines = sink.Events
            .Where(e => e.IsFromNamespace("MedConnect.Application.Common.Behaviors.RetryBehavior"))
            .ToList();
        foreach (var line in retryLines)
        {
            _output.WriteLine($"[{line.Level}] {line.RenderMessage()}");
        }

        retryLines.Should().Contain(
            e => e.Level == LogEventLevel.Debug && e.Text("FailureType") == "TransientConflictException",
            "請求真的撞上了 InnoDB 死結（1213），被 UnitOfWork 轉成 TransientConflictException 後重試");
        response.StatusCode.Should().Be(HttpStatusCode.Created, "重試之後應該成功");

        var errors = sink.Events.Where(e => e.Level >= LogEventLevel.Error).ToList();
        foreach (var error in errors)
        {
            _output.WriteLine($"UNEXPECTED ERROR LOG: {error.SourceContext()} #{error.Properties.GetValueOrDefault("EventId")} exc={error.Exception?.GetType().Name ?? "-"} {error.RenderMessage().Split('\n')[0]}");
        }

        errors.Should().BeEmpty("死結被重試吸收，不該留下任何 Error 層級的日誌（EF 自己的兩筆 Error 已被過濾）");
    }

    private static string RootConnectionString(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var appConnectionString = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>().Database.GetConnectionString()!;

        // Testcontainers 把同一組密碼設成 root 密碼；要 root 才有權限查 information_schema.innodb_trx 裡別人的交易。
        return new MySqlConnectionStringBuilder(appConnectionString)
        {
            UserID = "root",
            Password = MySqlTestContainer.Password,
        }.ConnectionString;
    }

    private static async Task Execute(MySqlConnection connection, MySqlTransaction transaction, string sql)
    {
        await using var command = new MySqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WaitUntilSomeTransactionIsWaitingForALockAsync(string rootConnectionString)
    {
        await using var observer = new MySqlConnection(rootConnectionString);
        await observer.OpenAsync();

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await using var command = new MySqlCommand(
                "SELECT COUNT(*) FROM performance_schema.data_lock_waits", observer);
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) > 0)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("請求沒有在 15 秒內進入等待鎖的狀態，死結情境沒有成形。");
    }
}
