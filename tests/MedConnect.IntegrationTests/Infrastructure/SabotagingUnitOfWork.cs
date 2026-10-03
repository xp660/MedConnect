using MedConnect.Application.Abstractions;
using MedConnect.Infrastructure.Persistence;

namespace MedConnect.IntegrationTests.Infrastructure;

/// <summary>
/// 跨 scope 共用的剖面：記錄 RetryBehavior 重試了幾次，並讓測試能在「Handler 已經讀完資料、
/// 但交易還沒開始」的那個精確時間點，插入一個別的請求的寫入。
///
/// 為什麼需要它：要證明 RetryBehavior 在同一個 scope 內重試時能正確運作，必須「確定性地」讓第一次嘗試
/// 因樂觀鎖失敗，而不是靠 50 個請求碰運氣。Booking / Cancel 的 Handler 都是「先讀、domain 檢查、
/// 才呼叫 ExecuteInTransactionAsync」，所以在 ExecuteInTransactionAsync 開頭插入競爭者的寫入，
/// 剛好就是「讀到舊資料之後被別人搶先」。
/// </summary>
public sealed class RetryProbe
{
    private int _beforeFirstTransactionFired;
    private int _resetTrackingCalls;
    private int _transactionCalls;

    /// <summary>只會在整個 provider 的第一次 ExecuteInTransactionAsync 之前執行一次（含競爭者自己的請求）。</summary>
    public Func<Task>? BeforeFirstTransaction { get; set; }

    /// <summary>每次交易之前都執行（用來模擬「每次嘗試都被搶先」的耗盡情境）。</summary>
    public Func<Task>? BeforeEveryTransaction { get; set; }

    /// <summary>RetryBehavior 重試前呼叫 ResetTracking 的次數 = 重試次數（競爭者請求不會重試）。</summary>
    public int ResetTrackingCalls => _resetTrackingCalls;

    /// <summary>所有 scope 加總的 ExecuteInTransactionAsync 呼叫次數（含競爭者）。</summary>
    public int TransactionCalls => _transactionCalls;

    internal void NoteResetTracking() => Interlocked.Increment(ref _resetTrackingCalls);

    internal async Task OnTransactionStartingAsync()
    {
        Interlocked.Increment(ref _transactionCalls);

        // 先把旗標立起來再執行 hook：hook 內的競爭者請求自己也會走到這裡，不能再次觸發。
        if (BeforeFirstTransaction is { } once && Interlocked.Exchange(ref _beforeFirstTransactionFired, 1) == 0)
        {
            await once();
        }

        if (BeforeEveryTransaction is { } every)
        {
            await every();
        }
    }
}

public sealed class SabotagingUnitOfWork : IUnitOfWork
{
    private readonly UnitOfWork _inner;
    private readonly RetryProbe _probe;

    public SabotagingUnitOfWork(UnitOfWork inner, RetryProbe probe)
    {
        _inner = inner;
        _probe = probe;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _inner.SaveChangesAsync(cancellationToken);

    public void ResetTracking()
    {
        _probe.NoteResetTracking();
        _inner.ResetTracking();
    }

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        await _probe.OnTransactionStartingAsync();
        return await _inner.ExecuteInTransactionAsync(operation, cancellationToken);
    }
}
