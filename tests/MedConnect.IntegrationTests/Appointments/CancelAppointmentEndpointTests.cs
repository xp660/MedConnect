using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MedConnect.Application.Abstractions;
using MedConnect.Domain.Entities;
using MedConnect.Domain.Enums;
using MedConnect.Infrastructure.Persistence;
using MedConnect.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedConnect.IntegrationTests.Appointments;

/// <summary>
/// 走真正的 HTTP pipeline：驗證路由（POST /{id}/cancel）、[Authorize]、從 JWT 讀 patientId、
/// 以及 GlobalExceptionHandler 對 404 APPOINTMENT_NOT_FOUND / 409 ALREADY_CANCELLED 的對映。
/// 取消的交易/鎖定順序/併發正確性由 CancelAppointmentTests 負責，這裡不重複。
/// </summary>
public sealed class CancelAppointmentEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public CancelAppointmentEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_cancel_without_bearer_token_returns_401()
    {
        var response = await _factory.CreateClient().PostAsync("/api/v1/appointments/1/cancel", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_cancel_by_the_owner_returns_200_and_a_second_cancel_returns_409_ALREADY_CANCELLED()
    {
        var client = await CreateAuthenticatedClientForNewPatientAsync();
        var appointmentId = await BookFirstSeededSlotAsync(client);

        var first = await client.PostAsync($"/api/v1/appointments/{appointmentId}/cancel", content: null);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await first.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("appointmentId").GetInt64().Should().Be(appointmentId);
        body.GetProperty("status").GetInt32().Should().Be((int)AppointmentStatus.Cancelled);
        body.TryGetProperty("cancelledAtUtc", out _).Should().BeTrue();

        var second = await client.PostAsync($"/api/v1/appointments/{appointmentId}/cancel", content: null);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errorCode").GetString().Should().Be("ALREADY_CANCELLED");
    }

    [Fact]
    public async Task Post_cancel_for_someone_elses_or_a_nonexistent_appointment_returns_the_same_404()
    {
        var ownerClient = await CreateAuthenticatedClientForNewPatientAsync();
        var appointmentId = await BookFirstSeededSlotAsync(ownerClient);
        var strangerClient = await CreateAuthenticatedClientForNewPatientAsync();

        var notYours = await strangerClient.PostAsync($"/api/v1/appointments/{appointmentId}/cancel", content: null);
        var missing = await strangerClient.PostAsync("/api/v1/appointments/999999999/cancel", content: null);

        // 防 Enumeration：呼叫端從狀態碼與 errorCode 完全無法分辨「存在但不是你的」與「根本不存在」。
        notYours.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var notYoursBody = await notYours.Content.ReadFromJsonAsync<JsonElement>();
        var missingBody = await missing.Content.ReadFromJsonAsync<JsonElement>();
        notYoursBody.GetProperty("errorCode").GetString().Should().Be("APPOINTMENT_NOT_FOUND");
        missingBody.GetProperty("errorCode").GetString().Should().Be("APPOINTMENT_NOT_FOUND");

        // 別人的預約不可被動到：擁有者仍然可以取消它。
        var ownerCancel = await ownerClient.PostAsync($"/api/v1/appointments/{appointmentId}/cancel", content: null);
        ownerCancel.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpClient> CreateAuthenticatedClientForNewPatientAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var user = new User($"cancel-{Guid.NewGuid():N}@cancel-endpoint.test", "not-a-real-password-hash", UserRole.Patient, now);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var patient = new Patient(user.Id, "Cancel Endpoint Patient", now);
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        var token = tokenService.GenerateToken(user, patient.Id);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }

    private async Task<long> BookFirstSeededSlotAsync(HttpClient authenticatedClient)
    {
        long slotId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
            slotId = await db.ScheduleSlots.AsNoTracking().Select(s => s.Id).FirstAsync();
        }

        var response = await authenticatedClient.PostAsJsonAsync("/api/v1/appointments", new { slotId });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("appointmentId").GetInt64();
    }
}
