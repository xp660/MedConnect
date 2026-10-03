using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MedConnect.Application.Common.Auth;
using MedConnect.Domain.Enums;

namespace MedConnect.Infrastructure.Security;

/// <summary>
/// JWT payload 的 claims 組裝方式，只有這一份：簽發端（JwtTokenService）與測試用的 token 偽造 helper
/// 都呼叫它，確保兩邊的 claims 結構永遠一致——否則簽發端新增/改名一個 claim，測試偽造的 token 卻沒跟上，
/// 測試會在「token 長得跟真的不一樣」的前提下通過或失敗，而不會有任何編譯期警告。
///
/// 放在 Infrastructure 而不是 Application：JwtRegisteredClaimNames 來自 JWT 套件，
/// 只有 Infrastructure 引用它（architecture-plan.md §4.2）。
/// </summary>
public static class JwtClaimsBuilder
{
    // architecture-plan.md §7.1：payload 至少含 sub、patient_id、role、jti（exp 由簽發時帶入）。
    // role 用 ClaimTypes.Role：這是 ASP.NET Core 預設的 role claim 型別。
    public static IReadOnlyList<Claim> BuildClaims(long userId, long patientId, UserRole role) =>
    [
        new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
        new Claim(JwtClaimNames.PatientId, patientId.ToString()),
        new Claim(ClaimTypes.Role, role.ToString()),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
    ];
}
