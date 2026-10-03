using System.IdentityModel.Tokens.Jwt;
using System.Text;
using MedConnect.Application.Abstractions;
using MedConnect.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MedConnect.Infrastructure.Security;

/// <summary>
/// architecture-plan.md §7.1：只負責簽發。payload 的 claims 組裝見 JwtClaimsBuilder。
/// 驗證不在這裡：Api 的 JwtBearer 驗證（Program.cs）與這裡共用同一份 JwtOptions，
/// 所以 Infrastructure 刻意不引用 Microsoft.AspNetCore.Authentication.JwtBearer。
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

        var claims = JwtClaimsBuilder.BuildClaims(user.Id, patientId, user.Role);

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
