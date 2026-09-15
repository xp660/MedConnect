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
    public static async Task SeedAsync(MedConnectDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken = default)
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

        // 1c 手動測試訂位流程用的固定病人：1c 還沒有 Login/JWT（1d 才做），沒有其他管道能產生
        // Patient。PasswordHash 只是滿足 users 表的 NOT NULL 約束，不是真的雜湊密碼，1d 接上真正
        // 的密碼雜湊機制前，這個帳號不能、也不應該被拿來登入。
        var testUser = new User("test-patient@medconnect.local", "not-a-real-password-hash", UserRole.Patient, now);
        db.Users.Add(testUser);
        await db.SaveChangesAsync(cancellationToken);

        var testPatient = new Patient(testUser.Id, "Test Patient", now);
        db.Patients.Add(testPatient);
        await db.SaveChangesAsync(cancellationToken);
    }
}
