using MedConnect.Application.Abstractions;
using MedConnect.Application.Common.Exceptions;

namespace MedConnect.IntegrationTests.Infrastructure;

/// <summary>每次交易都以並發衝突（或死結）失敗：用來決定性地走到「重試用盡」。</summary>
public sealed class AlwaysConflictingUnitOfWork : IUnitOfWork
{
    private readonly bool _deadlock;
    private int _transactionCalls;
    private int _resetTrackingCalls;

    public AlwaysConflictingUnitOfWork(bool deadlock) => _deadlock = deadlock;

    public int TransactionCalls => _transactionCalls;
    public int ResetTrackingCalls => _resetTrackingCalls;
    public string? LastFailureType { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void ResetTracking() => Interlocked.Increment(ref _resetTrackingCalls);

    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _transactionCalls);
        Exception failure = _deadlock
            ? new TransientConflictException(new Exception("forced deadlock"))
            : new ConcurrencyConflictException(new Exception("forced version conflict"));
        LastFailureType = failure.GetType().Name;
        throw failure;
    }
}

/// <summary>任何交易都丟一個沒有被對映的例外：用來走到 500 路徑。</summary>
public sealed class ThrowingUnitOfWork : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("forced unmapped failure");

    public void ResetTracking()
    {
    }

    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("forced unmapped failure");
}
