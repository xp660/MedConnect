using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MedConnect.Application.Abstractions;
using MedConnect.Domain.Entities;
using MedConnect.Domain.Enums;
using MedConnect.Infrastructure.Persistence;
using MedConnect.Infrastructure.Persistence.Seed;
using MedConnect.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedConnect.IntegrationTests.Validation;

/// <summary>
/// 走真正的 HTTP pipeline 驗證 ValidationBehavior 與 model-binding 兩個 400 來源都收斂成同一種形狀：
/// application/problem+json、errorCode = VALIDATION_FAILED、traceId、errors（欄位 → 訊息陣列）。
/// 每個 case 改動前的實際回應見 characterization 紀錄（ZzCharacterizationProbe 的輸出）。
/// </summary>
public sealed class ValidationEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public ValidationEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ValidationBehavior 產生的 400：errors 的 key 是驗證器指定的對外欄位名，可以精確斷言。
    [Theory]
    [InlineData("POST", "/api/v1/appointments", "{\"slotId\":0}", "slotId")]
    [InlineData("POST", "/api/v1/appointments", "{\"slotId\":-1}", "slotId")]
    [InlineData("POST", "/api/v1/appointments", "{}", "slotId")]
    [InlineData("POST", "/api/v1/appointments/0/cancel", null, "id")]
    [InlineData("POST", "/api/v1/appointments/-5/cancel", null, "id")]
    [InlineData("GET", "/api/v1/schedule-slots?doctorId=0&date=2026-10-05", null, "doctorId")]
    [InlineData("GET", "/api/v1/schedule-slots?doctorId=1", null, "date")]
    [InlineData("GET", "/api/v1/schedule-slots", null, "doctorId,date")]
    [InlineData("POST", "/api/v1/auth/login", "{\"email\":\"\",\"password\":\"x\"}", "email")]
    [InlineData("POST", "/api/v1/auth/login", "{\"email\":\"a@b.com\",\"password\":\"\"}", "password")]
    [InlineData("POST", "/api/v1/auth/login", "{\"email\":\"\",\"password\":\"\"}", "email,password")]
    public async Task Business_validation_failures_return_400_VALIDATION_FAILED_naming_the_offending_fields(
        string method, string url, string? body, string expectedFieldsCsv)
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await SendAsync(client, method, url, body);

        await AssertValidationFailedAsync(response, expectedFieldsCsv.Split(','));
    }

    [Fact]
    public async Task Login_email_longer_than_the_column_limit_returns_400()
    {
        var client = _factory.CreateClient();
        var body = $"{{\"email\":\"{new string('a', 300)}@x.com\",\"password\":\"x\"}}";

        var response = await SendAsync(client, "POST", "/api/v1/auth/login", body);

        await AssertValidationFailedAsync(response, "email");
    }

    // 這幾種在 ValidationBehavior 之前就被 [ApiController] 的 model binding 擋下（改動前是一個沒有
    // errorCode、traceId 形狀也不同的預設 400）。key 的細節是框架行為，這裡只釘住「形狀統一」。
    [Theory]
    [InlineData("POST", "/api/v1/appointments", "{")]
    [InlineData("POST", "/api/v1/appointments", "{\"slotId\":\"abc\"}")]
    [InlineData("GET", "/api/v1/schedule-slots?doctorId=1&date=notadate", null)]
    [InlineData("GET", "/api/v1/schedule-slots?doctorId=abc&date=2026-10-05", null)]
    public async Task Model_binding_failures_return_the_same_400_shape_as_business_validation(
        string method, string url, string? body)
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await SendAsync(client, method, url, body);

        var problem = await AssertValidationFailedAsync(response);
        problem.GetProperty("errors").EnumerateObject().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Model_binding_error_keys_are_camelCased_to_match_the_JSON_field_names()
    {
        // 改動前這裡是 PascalCase 的 "Email" / "Password"（ASP.NET 預設）。
        var client = _factory.CreateClient();

        var response = await SendAsync(client, "POST", "/api/v1/auth/login", "{}");

        await AssertValidationFailedAsync(response, "email", "password");
    }

    [Fact]
    public async Task Valid_requests_are_not_affected_by_validation()
    {
        var client = _factory.CreateClient();

        var login = await SendAsync(client, "POST", "/api/v1/auth/login",
            $"{{\"email\":\"test-patient@medconnect.local\",\"password\":\"{DatabaseSeeder.TestPatientPassword}\"}}");
        var slots = await SendAsync(client, "GET", "/api/v1/schedule-slots?doctorId=1&date=2026-10-05", null);

        login.StatusCode.Should().Be(HttpStatusCode.OK);
        slots.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_well_formed_id_that_does_not_exist_is_still_404_not_400()
    {
        // 驗證只管「形狀」：id > 0 但查無此人/此預約，仍然是 Handler 判斷的 404。
        var client = await CreateAuthenticatedClientAsync();

        var doctor = await SendAsync(client, "GET", "/api/v1/schedule-slots?doctorId=999999&date=2026-10-05", null);
        var appointment = await SendAsync(client, "POST", "/api/v1/appointments/999999999/cancel", null);

        doctor.StatusCode.Should().Be(HttpStatusCode.NotFound);
        appointment.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Every_error_response_is_application_problem_json_with_errorCode_and_traceId()
    {
        // 改動前，被 GlobalExceptionHandler 對映的錯誤（404/409/401…）實際回的是 application/json，
        // 與 architecture-plan.md §7 規定的 application/problem+json 不符（WriteAsJsonAsync 會覆寫
        // 先前設好的 Content-Type）。
        var client = await CreateAuthenticatedClientAsync();
        var anonymous = _factory.CreateClient();

        var notFound = await SendAsync(client, "GET", "/api/v1/schedule-slots?doctorId=999999&date=2026-10-05", null);
        var unauthorized = await SendAsync(anonymous, "POST", "/api/v1/auth/login",
            "{\"email\":\"nobody@medconnect.local\",\"password\":\"wrong\"}");

        foreach (var (response, expectedErrorCode) in new[]
                 {
                     (notFound, "DOCTOR_NOT_FOUND"),
                     (unauthorized, "INVALID_CREDENTIALS"),
                 })
        {
            response.ShouldBeProblemJson();
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            problem.GetProperty("errorCode").GetString().Should().Be(expectedErrorCode);
            problem.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        }
    }

    private static async Task<JsonElement> AssertValidationFailedAsync(HttpResponseMessage response, params string[] expectedFields)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.ShouldBeProblemJson();

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errorCode").GetString().Should().Be("VALIDATION_FAILED");
        problem.GetProperty("status").GetInt32().Should().Be(400);
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();

        if (expectedFields.Length > 0)
        {
            problem.GetProperty("errors").EnumerateObject().Select(p => p.Name)
                .Should().BeEquivalentTo(expectedFields);
        }

        return problem;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string url, string? body)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return client.SendAsync(request);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var user = new User($"validation-{Guid.NewGuid():N}@validation.test", "not-a-real-password-hash", UserRole.Patient, now);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var patient = new Patient(user.Id, "Validation Patient", now);
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokenService.GenerateToken(user, patient.Id).AccessToken);
        return client;
    }
}
