using System.Net;
using FluentAssertions;
using MedConnect.Infrastructure.Persistence.Seed;
using MedConnect.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using Xunit;

namespace MedConnect.IntegrationTests.Logging;

/// <summary>
/// 啟動開關（architecture-plan.md §0 v1.13）在 Development 環境（整合測試與本機開發用的環境）的行為：
/// 與開關出現之前完全相同——兩個開關都是 true，所以 migrate 與 seed 照常發生。
/// 共用 LoggingCollection 的容器；每個測試用自己的 host 與捕捉 sink。
/// </summary>
[Collection(LoggingCollection.Name)]
public sealed class StartupSwitchesLoggingTests
{
    private readonly ApiWebApplicationFactory _factory;

    public StartupSwitchesLoggingTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Development_configuration_turns_both_switches_on_so_local_development_and_tests_behave_as_before()
    {
        using var scope = _factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        configuration["Database:MigrateOnStartup"].Should().Be("True");
        configuration["Database:SeedDemoData"].Should().Be("True");
        configuration["Hosting:UseHttpsRedirection"].Should().Be("True", "本機 dotnet run 的行為不變：預設仍然啟用 HTTPS 轉址");
    }

    [Fact]
    public void Enabling_SeedDemoData_logs_one_Warning_that_contains_neither_the_password_nor_the_email()
    {
        var sink = new CapturingLogSink();
        using var factory = _factory.WithLogCapture(sink, verbose: false);

        _ = factory.Services; // 啟動 host（Development：兩個開關都是 true）

        var warnings = sink.Events.Where(e => e.Level == LogEventLevel.Warning && e.RenderMessage().Contains("SeedDemoData")).ToList();
        var warning = warnings.Should().ContainSingle().Subject;
        warning.RenderMessage().Should().Contain("publicly known password");
        warning.RenderMessage().Should().NotContain("creating", "seeder 遇到已有資料會直接跳過，措辭用「可能」而不是「正在建立」");
        var everything = string.Join(" ", sink.Events.Select(e => e.Everything()));
        everything.Should().NotContain(DatabaseSeeder.TestPatientPassword);
        everything.Should().NotContain("test-patient@medconnect.local");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task UseHttpsRedirection_defaults_to_enabled_and_can_be_switched_off(string? setting, bool expectRedirectionMiddlewareToRun)
    {
        var sink = new CapturingLogSink();
        using var factory = _factory.WithLogCapture(sink, verbose: false, extraConfiguration: new Dictionary<string, string?>
        {
            // 測試基底設定把這個類別調到 Error 避免雜訊；這裡要看見它，所以調回來。
            ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore.HttpsPolicy"] = "Debug",
            ["Hosting:UseHttpsRedirection"] = setting,
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // TestServer 沒有 https 埠：啟用時 middleware 每個請求都會記「Failed to determine the https port」；關閉時根本不在 pipeline 裡。
        sink.Events.Any(e => e.IsFromNamespace("Microsoft.AspNetCore.HttpsPolicy")).Should().Be(expectRedirectionMiddlewareToRun);
    }
}
