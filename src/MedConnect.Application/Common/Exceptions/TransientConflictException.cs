namespace MedConnect.Application.Common.Exceptions;

/// <summary>
/// Infrastructure 層在 SaveChanges 攔截到 MySQL deadlock（error 1213）時轉譯成這個型別往上拋，
/// 讓 Application/Api 層不需要依賴 MySqlConnector 型別（architecture-plan.md §4.2/§5.2）。
///
/// 刻意與 <see cref="ConcurrencyConflictException"/> 分開，而不是共用同一個型別：兩者雖然對
/// 客戶端而言都是「可重試」，但成因在結構上不同——ConcurrencyConflictException 來自
/// `DbUpdateConcurrencyException`（`WHERE version = ?` 比對失敗，語意上是「有人先搶走了」）；
/// 這個型別來自 InnoDB 偵測到鎖的循環等待而主動犧牲其中一個交易（語意上是「兩邊互相卡住，
/// 我沒真的輸，只是被資料庫選中要重來」）。混用同一個型別會讓未來想針對其中一種調整重試策略
/// （例如只對死結重試、不對真正的版本衝突重試）時，必須先在 Infrastructure 層重新拆開兩種成因，
/// 不如一開始就分開。
/// </summary>
public sealed class TransientConflictException : Exception
{
    public TransientConflictException(Exception innerException)
        : base("The operation was aborted due to a transient database conflict (deadlock). Retry the operation.", innerException)
    {
    }
}
