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
/// 直接在真實 MySQL 上驗證 Login 用的單次 LEFT JOIN 查詢：有 Patient、沒有 Patient、查無此 email
/// 三種情況必須能被分開（尤其「有 User 沒 Patient」不能被 INNER JOIN 吞成「查無此 email」）。
/// </summary>
[Collection(MySqlContainerCollection.Name)]
public class UserRepositoryTests
{
    private readonly MySqlContainerFixture _fixture;

    public UserRepositoryTests(MySqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetWithPatientIdByEmailAsync_ForAUserWithAPatient_ReturnsTheUserAndThePatientId()
    {
        var (email, userId, patientId) = await SeedUserAsync(withPatient: true);

        var (result, _) = await QueryAsync(email);

        result.Should().NotBeNull();
        result!.User.Id.Should().Be(userId);
        result.User.Email.Should().Be(email);
        result.User.PasswordHash.Should().Be("hash-for-" + email, "Login 需要 PasswordHash 來驗證密碼");
        result.User.Role.Should().Be(UserRole.Patient, "簽發 token 需要 Role");
        result.PatientId.Should().Be(patientId);
    }

    [Fact]
    public async Task GetWithPatientIdByEmailAsync_ForAUserWithoutAPatient_ReturnsTheUserWithANullPatientId()
    {
        // LEFT JOIN 的重點：這個 User 必須仍然被回傳（PatientId = null），不能因為沒有 Patient 就消失，
        // 否則「缺 Patient 記錄」會被誤判成「查無此帳號」，Login 的錯誤處理（驗證密碼後才丟資料不一致例外）就失效了。
        var (email, userId, _) = await SeedUserAsync(withPatient: false);

        var (result, _) = await QueryAsync(email);

        result.Should().NotBeNull();
        result!.User.Id.Should().Be(userId);
        result.PatientId.Should().BeNull();
    }

    [Fact]
    public async Task GetWithPatientIdByEmailAsync_ForAnUnknownEmail_ReturnsNull()
    {
        var (result, _) = await QueryAsync($"nobody-{Guid.NewGuid():N}@users.test");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetWithPatientIdByEmailAsync_DoesNotTrackTheEntitiesItReads()
    {
        var (email, _, _) = await SeedUserAsync(withPatient: true);

        var (_, trackedEntries) = await QueryAsync(email);

        trackedEntries.Should().Be(0, "純讀取的路徑不該把 User 放進 change tracker（AsNoTracking）");
    }

    private async Task<(UserWithPatientId? Result, int TrackedEntries)> QueryAsync(string email)
    {
        await using var scope = _fixture.ScopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();

        var result = await repository.GetWithPatientIdByEmailAsync(email, CancellationToken.None);

        return (result, db.ChangeTracker.Entries().Count());
    }

    private async Task<(string Email, long UserId, long? PatientId)> SeedUserAsync(bool withPatient)
    {
        await using var scope = _fixture.ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var email = $"user-{Guid.NewGuid():N}@users.test";
        var user = new User(email, "hash-for-" + email, UserRole.Patient, now);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        long? patientId = null;
        if (withPatient)
        {
            var patient = new Patient(user.Id, "Repository Test Patient", now);
            db.Patients.Add(patient);
            await db.SaveChangesAsync();
            patientId = patient.Id;
        }

        return (email, user.Id, patientId);
    }
}
