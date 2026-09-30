using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MedConnect.Application.Abstractions;
using MedConnect.Domain.Entities;
using MedConnect.Domain.Enums;
using MedConnect.Infrastructure.Persistence;
using MedConnect.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace MedConnect.IntegrationTests.Auth;

/// <summary>
/// architecture-plan.md §12 第 6 項的最後一塊拼圖：這是全案第一次透過真正的
/// ASP.NET Core middleware pipeline（WebApplicationFactory，而非直接呼叫 Handler）
/// 驗證 JWT Bearer 驗證真的擋住/放行請求，以及 FallbackPolicy 的 fail-safe 預設行為。
/// </summary>
public sealed class JwtAuthenticationTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public JwtAuthenticationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_appointments_without_bearer_token_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/appointments", new { slotId = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_appointments_with_expired_token_returns_401()
    {
        var client = _factory.CreateClient();
        // 用跟伺服器端相同的簽章密鑰簽，簽名本身合法——模擬「65 分鐘前簽發、效期只有 60 分鐘」
        // 的 token，此刻已經過期 5 分鐘。單純測「過期」這一項驗證規則，不跟簽名驗證混在一起。
        var now = DateTime.UtcNow;
        var expiredToken = CreateToken(patientId: 1, notBefore: now.AddMinutes(-65), expires: now.AddMinutes(-5));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await client.PostAsJsonAsync("/api/v1/appointments", new { slotId = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_appointments_with_wrong_signature_returns_401()
    {
        var client = _factory.CreateClient();
        // 用一把跟伺服器端不同的密鑰簽、且效期正常——單純測「簽名不符」這一項驗證規則。
        var now = DateTime.UtcNow;
        var wronglySignedToken = CreateToken(
            patientId: 1,
            notBefore: now,
            expires: now.AddMinutes(60),
            signingKey: "a-completely-different-signing-key-the-server-never-configured");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", wronglySignedToken);

        var response = await client.PostAsJsonAsync("/api/v1/appointments", new { slotId = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_appointments_with_valid_token_succeeds_and_uses_patientId_from_token_not_a_hardcoded_value()
    {
        // 刻意另外建立第二個病人（DatabaseSeeder 預設只 seed 一個 id=1 的病人）。
        // 如果 Controller 還殘留舊的 `const long temporaryPatientId = 1` 寫死值，
        // 這個測試會因為 response.patientId 落在錯的 id 上而抓到——用 id=1 測不出這種回歸。
        var secondPatient = await CreateSecondPatientAsync();

        using var scope = _factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var token = tokenService.GenerateToken(secondPatient.User, secondPatient.Patient.Id);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var slotId = await GetFirstSeededSlotIdAsync();
        var response = await client.PostAsJsonAsync("/api/v1/appointments", new { slotId });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("patientId").GetInt64().Should().Be(secondPatient.Patient.Id);
        body.GetProperty("patientId").GetInt64().Should().NotBe(1, "id=1 是 DatabaseSeeder 的預設病人，用它測不出「是否還寫死成 1」這種回歸");
    }

    [Fact]
    public async Task Post_auth_login_without_bearer_token_is_still_reachable()
    {
        // 確認全域 FallbackPolicy（RequireAuthenticatedUser）沒有連 Login 本身都一併擋住——
        // 用故意錯誤的密碼呼叫，只是為了證明「這個端點有被路由到、走到業務邏輯」，
        // 回應是 401 INVALID_CREDENTIALS（業務層錯誤）而不是被驗證 middleware 擋在門口。
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = "test-patient@medconnect.local",
            password = "definitely-wrong-password",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errorCode").GetString().Should().Be("INVALID_CREDENTIALS");
    }

    private static string CreateToken(long patientId, DateTime notBefore, DateTime expires, string? signingKey = null)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "1"),
            new Claim("patient_id", patientId.ToString()),
            new Claim(ClaimTypes.Role, UserRole.Patient.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? ApiWebApplicationFactory.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: ApiWebApplicationFactory.Issuer,
            audience: ApiWebApplicationFactory.Audience,
            claims: claims,
            notBefore: notBefore,
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<long> GetFirstSeededSlotIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        return await db.ScheduleSlots.AsNoTracking().Select(s => s.Id).FirstAsync();
    }

    private async Task<(User User, Patient Patient)> CreateSecondPatientAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var now = timeProvider.GetUtcNow();

        var user = new User("second-patient@medconnect.local", passwordHasher.Hash("Whatever1!"), UserRole.Patient, now);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var patient = new Patient(user.Id, "Second Patient", now);
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        return (user, patient);
    }
}
