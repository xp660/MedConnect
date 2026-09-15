using MedConnect.Application.Abstractions;
using MedConnect.Application.Common.Exceptions;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace MedConnect.Infrastructure.Persistence;

/// <summary>
/// architecture-plan.md §5.2：Booking 流程有兩種不同來源的衝突，不可用同一個 catch 區塊處理。
/// 這裡是唯一攔截、轉譯 EF/MySQL 例外的地方，Application/Api 層不需要認識 DbUpdateConcurrencyException
/// 或 MySqlException。
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private const int MySqlDuplicateEntryErrorNumber = 1062;
    private const string DuplicateActivePatientBookingIndexName = "ux_appt_slot_active_patient";

    private readonly MedConnectDbContext _dbContext;

    public UnitOfWork(MedConnectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (IsDuplicateActivePatientBooking(ex, out var slotId, out var patientId))
        {
            throw new DuplicateBookingException(slotId, patientId, ex);
        }
    }

    private static bool IsDuplicateActivePatientBooking(DbUpdateException ex, out long slotId, out long patientId)
    {
        slotId = 0;
        patientId = 0;

        if (ex.InnerException is not MySqlException { Number: MySqlDuplicateEntryErrorNumber } mySqlException
            || !mySqlException.Message.Contains(DuplicateActivePatientBookingIndexName, StringComparison.Ordinal))
        {
            return false;
        }

        // 不能假設「這次 SaveChanges 只有 Appointment 這一個 entry」——BookAppointmentHandler
        // 一定會同時讓 ScheduleSlot 變成 Modified（BookedCount++）跟新增一筆 Appointment，
        // 兩個 entry 本來就會一起出現在同一次 SaveChanges，要找的是「這次新增的那個 Appointment」，
        // 不是「唯一的那個 entry」。
        var appointmentEntry = ex.Entries
            .FirstOrDefault(e => e.State == EntityState.Added && e.Entity is Domain.Entities.Appointment);

        if (appointmentEntry is null)
        {
            return false;
        }

        var appointment = (Domain.Entities.Appointment)appointmentEntry.Entity;
        slotId = appointment.SlotId;
        patientId = appointment.PatientId;

        return true;
    }
}
