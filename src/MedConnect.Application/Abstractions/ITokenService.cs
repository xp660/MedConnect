using MedConnect.Domain.Entities;

namespace MedConnect.Application.Abstractions;

/// <summary>
/// architecture-plan.md §4.3：JWT 產生的介面放 Application，實作（System.IdentityModel.Tokens.Jwt）放 Infrastructure。
/// </summary>
public interface ITokenService
{
    GeneratedToken GenerateToken(User user, long patientId);
}

/// <summary>
/// ExpiresInSeconds 由產生當下的設定值決定，交由 Handler/Controller 直接回傳，
/// 避免 Controller 再用「到期時間 - 現在時間」重新計算一次造成誤差。
/// </summary>
public sealed record GeneratedToken(string AccessToken, int ExpiresInSeconds);
