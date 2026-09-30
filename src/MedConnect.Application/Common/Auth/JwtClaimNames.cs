namespace MedConnect.Application.Common.Auth;

/// <summary>
/// 自訂 JWT claim 名稱的單一來源。簽發端（Infrastructure 的 JwtTokenService）與讀取端
/// （Api 的 Controller、測試用的 token 產生 helper）都必須引用這裡，不可各自手打字串——
/// 三處各自維護同一個字面值，只要有一邊改名忘了同步，就會是一個編譯期完全抓不到、
/// 只會在執行期讀不到 claim 的 bug。
/// </summary>
public static class JwtClaimNames
{
    public const string PatientId = "patient_id";
}
