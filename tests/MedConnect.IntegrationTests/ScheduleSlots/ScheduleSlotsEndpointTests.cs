using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MedConnect.Infrastructure.Persistence;
using MedConnect.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedConnect.IntegrationTests.ScheduleSlots;

/// <summary>
/// 跟 JwtAuthenticationTests 用同一個 ApiWebApplicationFactory，走真正的 HTTP pipeline——
/// 這裡要驗證的重點是 [AllowAnonymous] 真的蓋過了 Program.cs 的全域 FallbackPolicy
/// （預設要求登入），以及 404 DOCTOR_NOT_FOUND 真的透過 GlobalExceptionHandler 對映出來。
/// </summary>
public sealed class ScheduleSlotsEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public ScheduleSlotsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_schedule_slots_without_bearer_token_succeeds_and_matches_ScheduleSlotDto_shape()
    {
        var (doctorId, date) = await GetSeededDoctorAndDateAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/schedule-slots?doctorId={doctorId}&date={date:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);
        body.GetArrayLength().Should().BeGreaterThan(0, "種子資料在這個日期應該至少有一個 Open 時段");

        var slot = body[0];
        slot.GetProperty("id").GetInt64().Should().BePositive();
        slot.GetProperty("doctorId").GetInt64().Should().Be(doctorId);
        slot.TryGetProperty("startUtc", out _).Should().BeTrue();
        slot.GetProperty("capacity").GetInt32().Should().BePositive();
        slot.GetProperty("availableCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        slot.GetProperty("status").GetString().Should().Be("Open");
    }

    [Fact]
    public async Task Get_schedule_slots_with_unknown_doctorId_returns_404_DoctorNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/schedule-slots?doctorId=999999&date=2026-10-05");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errorCode").GetString().Should().Be("DOCTOR_NOT_FOUND");
    }

    private async Task<(long DoctorId, DateOnly Date)> GetSeededDoctorAndDateAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        var slot = await db.ScheduleSlots.AsNoTracking().FirstAsync();
        return (slot.DoctorId, DateOnly.FromDateTime(slot.TimeSlot.StartUtc));
    }
}
