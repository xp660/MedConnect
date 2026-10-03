using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MedConnect.Application.Abstractions;
using MedConnect.Domain.Entities;
using MedConnect.Domain.Enums;
using MedConnect.Infrastructure.Persistence;
using MedConnect.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedConnect.IntegrationTests.Auth;

/// <summary>
/// 在真實 HTTP pipeline + 真實 MySQL 上驗證 Login 的硬性要求：先驗證密碼、才檢查 PatientId。
/// 單元測試用 mock 釘住了 Handler 的順序；這裡確認真實的查詢（單次 LEFT JOIN）加上真實的 BCrypt
/// 跑出來的外部行為也一樣——不知道密碼的人，看不出某個 email 的帳號缺了 Patient 記錄。
/// </summary>
public sealed class LoginEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string CorrectPassword = "Correct-Horse-1!";

    private readonly ApiWebApplicationFactory _factory;

    public LoginEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Wrong_password_for_a_user_without_a_patient_is_indistinguishable_from_an_unknown_email()
    {
        var email = await SeedUserWithoutPatientAsync();
        var client = _factory.CreateClient();

        var noPatientWrongPassword = await Login(client, email, "definitely-wrong");
        var unknownEmail = await Login(client, $"nobody-{Guid.NewGuid():N}@login.test", "definitely-wrong");

        foreach (var response in new[] { noPatientWrongPassword, unknownEmail })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            response.ShouldBeProblemJson();
        }

        var first = await noPatientWrongPassword.Content.ReadFromJsonAsync<JsonElement>();
        var second = await unknownEmail.Content.ReadFromJsonAsync<JsonElement>();
        first.GetProperty("errorCode").GetString().Should().Be("INVALID_CREDENTIALS");
        first.GetProperty("errorCode").GetString().Should().Be(second.GetProperty("errorCode").GetString());
        first.GetProperty("detail").GetString().Should().Be(second.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Correct_password_for_a_user_without_a_patient_is_a_500_not_masked_as_a_401()
    {
        // 缺 Patient 記錄是資料不一致的 bug，不是帳密錯誤：驗證通過之後才暴露出來，且不被吞成 401。
        var email = await SeedUserWithoutPatientAsync();
        var client = _factory.CreateClient();

        var response = await Login(client, email, CorrectPassword);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Correct_password_for_a_user_with_a_patient_logs_in()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var email = $"with-patient-{Guid.NewGuid():N}@login.test";
        var user = new User(email, hasher.Hash(CorrectPassword), UserRole.Patient, now);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.Patients.Add(new Patient(user.Id, "Login Test Patient", now));
        await db.SaveChangesAsync();

        var response = await Login(_factory.CreateClient(), email, CorrectPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
    }

    private async Task<string> SeedUserWithoutPatientAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var email = $"no-patient-{Guid.NewGuid():N}@login.test";
        db.Users.Add(new User(email, hasher.Hash(CorrectPassword), UserRole.Patient, now));
        await db.SaveChangesAsync();
        return email;
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
}
