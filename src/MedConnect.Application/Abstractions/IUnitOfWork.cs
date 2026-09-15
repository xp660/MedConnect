namespace MedConnect.Application.Abstractions;

/// <summary>
/// architecture-plan.md §9.3：只提供明確的 commit point，不抽象化資料庫本身。
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
