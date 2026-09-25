using MedConnect.Application.Abstractions;
using MedConnect.Domain.Entities;
using MedConnect.Domain.Enums;
using MedConnect.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MedConnect.Infrastructure.Persistence.Seed;

/// <summary>
/// MVP 不建模「排班樣板（Schedule）」，改用 seed 直接產生扁平的 ScheduleSlot，見 architecture-plan.md §3.1。
/// </summary>
public static class DatabaseSeeder
{
    /// <summary>
    /// 1d 手動測試登入用的固定測試帳密：test-patient@medconnect.local / Test1234!
    /// </summary>
    public const string TestPatientPassword = "Test1234!";

    public static async Task SeedAsync(
        MedConnectDbContext db,
        TimeProvider timeProvider,
        IPasswordHasher passwordHasher,
        CancellationToken cancellationToken = default)
    {
        if (await db.Doctors.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        var doctors = new[]
        {
            new Doctor("Wang Wei", "Cardiology", now),
            new Doctor("Chen Hui", "Dermatology", now)
        };
        db.Doctors.AddRange(doctors);
        await db.SaveChangesAsync(cancellationToken);

        var slots = new List<ScheduleSlot>();
        foreach (var doctor in doctors)
        {
            for (var day = 1; day <= 3; day++)
            {
                var start = now.UtcDateTime.Date.AddDays(day).AddHours(9);
                var timeSlot = new TimeSlot(start, start.AddMinutes(30));
                slots.Add(new ScheduleSlot(doctor.Id, timeSlot, capacity: 5, now));
            }
        }

        db.ScheduleSlots.AddRange(slots);
        await db.SaveChangesAsync(cancellationToken);

        // 1c 手動測試訂位流程用的固定病人。1d 接上真正的密碼雜湊機制後，改用 IPasswordHasher
        // 算出真實的 BCrypt hash，讓這個帳號可以拿來手動測試 POST /api/v1/auth/login
        // （帳密見 TestPatientPassword 常數：test-patient@medconnect.local / Test1234!）。
        var testUser = new User("test-patient@medconnect.local", passwordHasher.Hash(TestPatientPassword), UserRole.Patient, now);
        db.Users.Add(testUser);
        await db.SaveChangesAsync(cancellationToken);

        var testPatient = new Patient(testUser.Id, "Test Patient", now);
        db.Patients.Add(testPatient);
        await db.SaveChangesAsync(cancellationToken);
    }
}
