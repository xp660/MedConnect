namespace MedConnect.Application.Abstractions;

/// <summary>
/// architecture-plan.md §9.3：只提供明確的 commit point，不抽象化資料庫本身。
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 讓呼叫端能表達「這幾次 SaveChanges 屬於同一筆交易」，藉此控制語句送出的順序。
    ///
    /// 為什麼需要它（architecture-plan.md §0 v1.4）：Booking 一次會寫兩張表，
    /// EF Core 在沒有「新增的 principal → dependent」相依邊時，是按**資料表名稱字典序**
    /// 決定語句順序的，所以 `appointments` 的 INSERT 一定排在 `schedule_slots` 的 UPDATE 前面。
    /// INSERT 的 FK 檢查會先對 slot 那一列上 S 鎖，接著 UPDATE 又要把同一列升級成 X 鎖，
    /// 50 個併發請求彼此等待 → InnoDB 死結（已用 SHOW ENGINE INNODB STATUS 實測確認）。
    /// 把 UPDATE 拆成第一次 SaveChanges、INSERT 拆成第二次，並包在同一筆交易裡，
    /// 就能保證先拿 X 鎖再做 FK 檢查，死結消失而原子性不變。
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken);
}
