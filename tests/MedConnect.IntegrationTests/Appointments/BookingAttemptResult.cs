namespace MedConnect.IntegrationTests.Appointments;

/// <summary>
/// 一次模擬請求的結果。併發測試不能讓任何一個 Task 以例外收場——Task.WhenAll 只要有一個
/// Task faulted 就會提前把例外丟出來，其餘 49 個結果就再也拿不到，也就無從斷言「幾個成功、
/// 幾個失敗、失敗的 errorCode 是什麼」。所以預期內的兩種失敗要在 Task 內部被轉成資料。
/// </summary>
public record BookingAttemptResult(long PatientId, bool Success, string? ErrorCode);
