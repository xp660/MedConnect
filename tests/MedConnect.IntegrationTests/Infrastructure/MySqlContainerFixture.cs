using MedConnect.Application;
using MedConnect.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MySql;

namespace MedConnect.IntegrationTests.Infrastructure;

/// <summary>
/// architecture-plan.md §8.4：整合測試必須用 Testcontainers 起真的 MySQL。
///
/// 這個容器與 docker-compose.yml 裡那台「手動測試用」的 MySQL 完全獨立：不同 image 實例、
/// Testcontainers 自己指派的隨機 host port、隨機 database/帳密、測試結束即銷毀。兩者不共用
/// 任何狀態，所以跑測試不會弄髒手動測試的資料，反之亦然。
///
/// 服務註冊刻意走跟 Program.cs 一模一樣的路徑（AddApplication + AddInfrastructure），
/// 只把連線字串換成容器的。這樣測到的是「正式組裝出來的物件圖」——真的 DbContext、真的
/// ConcurrencyVersionInterceptor、真的 UnitOfWork、真的 MediatR pipeline——而不是測試裡
/// 手動 new 出來的一套平行宇宙。
/// </summary>
public sealed class MySqlContainerFixture : IAsyncLifetime
{
    // §8.6：固定 image tag，避免 latest 造成 flaky；與 docker-compose.yml 用同一個版本，
    // 確保「手動測到的行為」跟「自動測到的行為」是同一個 MySQL 版本的行為。
    private const string MySqlImage = "mysql:8.0.39";

    private readonly MySqlContainer _container = new MySqlBuilder(MySqlImage)
        .WithDatabase("medconnect")
        .WithUsername("medconnect")
        .WithPassword("medconnect")
        .Build();

    private ServiceProvider? _serviceProvider;

    /// <summary>
    /// 測試要用這個來開 scope。每個模擬請求都必須自己 CreateScope()，才會拿到各自獨立的
    /// DbContext——這正是併發測試要驗證的前提（§8.5：DbContext 必須是 Scoped）。
    /// </summary>
    public IServiceScopeFactory ScopeFactory =>
        (_serviceProvider ?? throw new InvalidOperationException("Fixture has not been initialised."))
            .GetRequiredService<IServiceScopeFactory>();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MedConnect"] = _container.GetConnectionString()
            })
            .Build();

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        _serviceProvider = services.BuildServiceProvider();

        // 只 migrate、不 seed（InfrastructureStartupExtensions.MigrateAsync）：併發測試要斷言
        // 精確的數字，資料庫裡不能有 DatabaseSeeder 產生的醫生/時段/病人混進來。
        await _serviceProvider.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}
