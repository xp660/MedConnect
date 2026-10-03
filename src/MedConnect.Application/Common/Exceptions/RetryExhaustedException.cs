namespace MedConnect.Application.Common.Exceptions;

/// <summary>
/// RetryBehavior 用盡重試次數、最後一次仍因 ConcurrencyConflictException 或 TransientConflictException 失敗。
/// 對映成 409 RETRY_EXHAUSTED，與單純的 CONCURRENCY_CONFLICT / TRANSIENT_CONFLICT 刻意分開：
/// 告訴客戶端「系統已經替你重試過了仍然失敗」，也保留「最後一次是樂觀鎖衝突還是死結」的區分
/// （InnerException 就是最後一次的原始例外）。
/// </summary>
public sealed class RetryExhaustedException : Exception
{
    public RetryExhaustedException(int attempts, Exception lastFailure)
        : base($"The operation still conflicted after {attempts} attempts.", lastFailure)
    {
        Attempts = attempts;
    }

    public int Attempts { get; }
}
