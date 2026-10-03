using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.MySql;

namespace MedConnect.IntegrationTests.Infrastructure;

/// <summary>
/// architecture-plan.md §12 第 6 項：1c 的所有測試（含 §8.5 招牌測試）都繞過 HTTP，直接用
/// IServiceScopeFactory + IMediator 呼叫 Handler，真正的 ASP.NET Core middleware pipeline
/// （這次要測的 JWT Bearer 驗證就活在這裡）在 1d 之前完全沒被跑過一次。這個 fixture 第一次
/// 讓測試真的透過 WebApplicationFactory 打 HTTP，經過完整 pipeline。
///
/// 刻意擁有自己獨立的 MySQL 容器，不與 MySqlContainerFixture 共用：兩者關注點完全不同
/// （HTTP 層 middleware pipeline vs. handler 併發），共用容器只會讓兩組測試的資料庫狀態
/// 互相干擾，換來的「省一次約 15 秒的容器啟動時間」不值得。
///
/// 刻意不用 dotnet user-secrets 裡的真實簽章密鑰：測試要能在任何機器/CI 上重現，
/// 不能依賴跑測試的人的本機使用者秘密存放區裡剛好有沒有設定這把鑰匙。
/// </summary>
public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // 僅供本測試類別內簽發「合法」與「簽名錯誤」的比對用 token，不是任何環境的真實密鑰。
    public const string SigningKey = "integration-test-only-signing-key-never-used-elsewhere-32bytes-min";
    public const string Issuer = "MedConnect.IntegrationTests";
    public const string Audience = "MedConnect.IntegrationTests.Api";

    private readonly MySqlContainer _container = MySqlTestContainer.Create();

    public Task InitializeAsync() => _container.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development：讓 Program.cs 既有的「Dev 環境自動 migrate + seed」區塊照常跑，
        // 直接沿用 DatabaseSeeder 產生的固定測試病人（test-patient@medconnect.local /
        // DatabaseSeeder.TestPatientPassword）當作 Login 測試的帳號，不必在這裡重寫一份。
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MedConnect"] = _container.GetConnectionString(),
                ["Jwt:SecretKey"] = SigningKey,
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:ExpiryMinutes"] = "60",
            });
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }
}
