using MedConnect.Application.Abstractions;
using MedConnect.Infrastructure.Persistence;
using MedConnect.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedConnect.Infrastructure;

/// <summary>
/// Api 依 architecture-plan.md §4.2 不可直接使用 DbContext/EF 型別，即使在 Program.cs 內也一樣；
/// 這裡把「套用 migration + seed」包成一個只吃 IServiceProvider 的方法，Api 只需呼叫它，
/// 不需要 using 任何 EF 型別。
/// </summary>
public static class InfrastructureStartupExtensions
{
    /// <summary>
    /// 只套用 migration，不塞 seed 資料。1c 的 Testcontainers 測試基礎設施應該呼叫這個，
    /// 讓測試資料庫只有正確的 schema，不會混入 DatabaseSeeder 產生的假醫生/假時段，
    /// 避免污染需要精確斷言數字的併發測試。
    /// </summary>
    public static async Task MigrateAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// 只塞 seed 資料，假設 migration 已經套用過。
    /// </summary>
    public static async Task SeedAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await DatabaseSeeder.SeedAsync(db, timeProvider, passwordHasher, cancellationToken);
    }

    /// <summary>
    /// 本機開發用：migrate + seed 一次做完。Program.cs 在 Development 環境呼叫這個，行為與 1b 時相同。
    /// </summary>
    public static async Task MigrateAndSeedAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await services.MigrateAsync(cancellationToken);
        await services.SeedAsync(cancellationToken);
    }
}
