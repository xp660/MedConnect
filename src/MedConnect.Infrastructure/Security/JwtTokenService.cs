using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MedConnect.Application.Abstractions;
using MedConnect.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MedConnect.Infrastructure.Security;

/// <summary>
/// architecture-plan.md §7.1：payload 至少含 sub、patient_id、role、jti、exp。
/// 只做簽發，不做驗證——驗證 middleware 是下一步（1d 的後半段），刻意不在這裡引入
/// Microsoft.AspNetCore.Authentication.JwtBearer。
/// </summary>
public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public GeneratedToken GenerateToken(User user, long patientId)
    {
        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(_options.ExpiryMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim("patient_id", patientId.ToString()),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        var expiresInSeconds = (int)(expiresAt - now).TotalSeconds;

        return new GeneratedToken(accessToken, expiresInSeconds);
    }
}
