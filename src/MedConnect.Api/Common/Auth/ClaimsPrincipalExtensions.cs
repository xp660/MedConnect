using System.Security.Claims;
using MedConnect.Application.Common.Auth;

namespace MedConnect.Api.Common.Auth;

/// <summary>
/// 從已驗證的 JWT claims 讀取 PatientId 的單一位置。Schedule Query、Cancel Appointment
/// 之後的 Controller 都應該重用這個 extension method，不要各自重新寫一次
/// User.FindFirst(JwtClaimNames.PatientId) + long.Parse 的樣板程式碼。
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static long GetPatientId(this ClaimsPrincipal user)
    {
        // 通過 [Authorize] 驗證的 Token 理論上不可能缺 JwtClaimNames.PatientId 這個 claim；
        // 如果真的缺了，這是預期外的異常，不吞成業務例外，讓它自然拋出。
        var patientIdClaim = user.FindFirst(JwtClaimNames.PatientId)?.Value
            ?? throw new InvalidOperationException($"Authenticated request is missing the required '{JwtClaimNames.PatientId}' claim.");

        return long.Parse(patientIdClaim);
    }
}
