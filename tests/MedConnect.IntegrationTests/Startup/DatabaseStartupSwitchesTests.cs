using System.Net;
using FluentAssertions;
using MedConnect.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MySqlConnector;
using Xunit;

namespace MedConnect.IntegrationTests.Startup;

/// <summary>
/// architecture-plan.md §0 v1.13：migrate 與 seed 由 Database:MigrateOnStartup / Database:SeedDemoData 決定，
/// 預設都是 false。這個類別擁有自己的（空的）MySQL 容器，而且「從不讓任何一個 host 成功 migrate」——
/// 所以每個測試開始時資料庫都是空的；會成功 migrate 的情境（Development）放在別的測試類別。
/// 一律在非 Development 環境啟動：Development 的 appsettings 會把兩個開關打開。
/// </summary>
public sealed class DatabaseStartupSwitchesTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public DatabaseStartupSwitchesTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private WebApplicationFactory<Program> ProductionHost(IReadOnlyDictionary<string, string?>? configuration = null) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration ?? new Dictionary<string, string?>()));
        });

    /// <summary>
    /// 兩種「什麼都不設」：隨 appsettings.json 出貨的預設值（明確寫著 false），以及連 appsettings.json 的鍵都不存在時
    /// 程式碼裡的預設值（用 in-memory 的 null 蓋掉）。兩處任何一處被改成 true 都必須讓這個測試紅燈。
    /// </summary>
    public static TheoryData<string, bool> DefaultModes => new()
    {
        { "as shipped in appsettings.json", false },
        { "keys absent (code default)", true },
    };

    [Theory]
    [MemberData(nameof(DefaultModes))]
    public async Task By_default_outside_Development_the_host_neither_migrates_nor_creates_any_user(string mode, bool removeKeys)
    {
        var overrides = removeKeys
            ? new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = null, ["Database:SeedDemoData"] = null }
            : null;
        using var host = ProductionHost(overrides);

        using var client = host.CreateClient();
        var health = await client.GetAsync("/health");

        host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName.Should().Be("Production", mode);
        health.StatusCode.Should().Be(HttpStatusCode.OK, "host 要真的啟動成功，下面的「沒有資料表」才不是空洞的結論");
        (await CountTablesAsync()).Should().Be(0, $"[{mode}] 沒有 migrate：資料庫裡不該有任何資料表（因此也不可能有任何使用者）");
    }

    [Fact]
    public async Task SeedDemoData_without_MigrateOnStartup_fails_clearly_instead_of_being_ignored()
    {
        using var host = ProductionHost(new Dictionary<string, string?>
        {
            ["Database:SeedDemoData"] = "true",
            ["Database:MigrateOnStartup"] = "false",
        });

        var act = () => host.CreateClient();

        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("Cannot seed demo data").And.Contain("pending migration").And.Contain("Database:MigrateOnStartup");
        (await CountTablesAsync()).Should().Be(0, "失敗時什麼都沒被建立");
    }

    private async Task<long> CountTablesAsync()
    {
        // 不經過 host（host 可能啟動失敗）：用容器的連線字串直接查。
        await using var connection = new MySqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE()", connection);

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
