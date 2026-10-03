using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MedConnect.Application.Abstractions;
using MedConnect.Application.Common.Exceptions;
using MedConnect.Domain.Entities;
using MedConnect.Domain.Enums;
using MedConnect.Infrastructure.Persistence;
using MedConnect.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace MedConnect.IntegrationTests.Appointments;

/// <summary>
/// 走真正的 HTTP pipeline：RetryBehavior 重試用盡後丟出的 RetryExhaustedException，
/// 必須由 GlobalExceptionHandler 對映成 409 + errorCode RETRY_EXHAUSTED（application/problem+json）。
/// 刻意與 CONCURRENCY_CONFLICT / TRANSIENT_CONFLICT 分開，讓客戶端知道「系統已經替你重試過了」。
/// </summary>
public sealed class RetryExhaustedEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public RetryExhaustedEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(false, "ConcurrencyConflictException")]
    [InlineData(true, "TransientConflictException")]
    public async Task Post_appointments_returns_409_RETRY_EXHAUSTED_when_every_attempt_conflicts(bool deadlock, string expectedLastFailure)
    {
        var unitOfWork = new AlwaysConflictingUnitOfWork(deadlock);
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddSingleton<IUnitOfWork>(unitOfWork);
        }));
        var client = await CreateAuthenticatedClientForNewPatientAsync(factory);
        var slotId = await FirstSeededSlotIdAsync(factory);

        var response = await client.PostAsJsonAsync("/api/v1/appointments", new { slotId });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.ShouldBeProblemJson();
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errorCode").GetString().Should().Be("RETRY_EXHAUSTED");
        unitOfWork.TransactionCalls.Should().Be(4, "首次執行 + 3 次重試");
        unitOfWork.ResetTrackingCalls.Should().Be(3);
        unitOfWork.LastFailureType.Should().Be(expectedLastFailure);
    }

    private sealed class AlwaysConflictingUnitOfWork : IUnitOfWork
    {
        private readonly bool _deadlock;
        private int _transactionCalls;
        private int _resetTrackingCalls;

        public AlwaysConflictingUnitOfWork(bool deadlock) => _deadlock = deadlock;

        public int TransactionCalls => _transactionCalls;
        public int ResetTrackingCalls => _resetTrackingCalls;
        public string? LastFailureType { get; private set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void ResetTracking() => Interlocked.Increment(ref _resetTrackingCalls);

        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _transactionCalls);
            Exception failure = _deadlock
                ? new TransientConflictException(new Exception("forced deadlock"))
                : new ConcurrencyConflictException(new Exception("forced version conflict"));
            LastFailureType = failure.GetType().Name;
            throw failure;
        }
    }

    private static async Task<HttpClient> CreateAuthenticatedClientForNewPatientAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var user = new User($"retry-{Guid.NewGuid():N}@retry-endpoint.test", "not-a-real-password-hash", UserRole.Patient, now);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var patient = new Patient(user.Id, "Retry Endpoint Patient", now);
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        var token = tokenService.GenerateToken(user, patient.Id);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }

    private static async Task<long> FirstSeededSlotIdAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        return await db.ScheduleSlots.AsNoTracking().Select(s => s.Id).FirstAsync();
    }
}
