namespace MedConnect.Application.Common.Exceptions;

/// <summary>
/// Infrastructure 層在 SaveChanges 攔截到 DbUpdateConcurrencyException 時轉譯成這個型別往上拋，
/// 讓 Application/Api 層不需要依賴 EF Core 型別（architecture-plan.md §4.2/§5.2）。可重試。
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(Exception innerException)
        : base("The resource was modified by another request. Retry the operation.", innerException)
    {
    }
}
