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
    public static async Task MigrateAndSeedAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        await db.Database.MigrateAsync(cancellationToken);
        await DatabaseSeeder.SeedAsync(db, timeProvider, cancellationToken);
    }
}
