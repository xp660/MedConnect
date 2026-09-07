using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MedConnect.Infrastructure.Persistence.Interceptors;

/// <summary>
/// MySQL 沒有 SQL Server rowversion 型別，改用應用程式維護的整數 version 欄位：
/// 在 SaveChanges 前把每個被修改實體的 concurrency token CurrentValue 設為 OriginalValue + 1，
/// 讓 EF 產生的 UPDATE ... WHERE version = {original} 能正確偵測併發衝突。
/// </summary>
public class ConcurrencyVersionInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        IncrementVersions(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        IncrementVersions(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void IncrementVersions(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            var versionProperty = entry.Properties
                .FirstOrDefault(p => p.Metadata.IsConcurrencyToken && p.Metadata.ClrType == typeof(int));

            if (versionProperty is null || versionProperty.OriginalValue is not int originalValue)
            {
                continue;
            }

            versionProperty.CurrentValue = originalValue + 1;
        }
    }
}
