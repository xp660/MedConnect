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
        // 注意：InnoDB 死結（MySqlError 1213）**不會**以 DbUpdateException 的形式冒出來，
        // 這點已用真實 MySQL 實測確認（不是照文件描述推測的）。因為 Booking 現在是在一個
        // 顯式交易（ExecuteInTransactionAsync）裡呼叫 SaveChangesAsync，EF Core 的
        // ExecutionStrategy 偵測到「這個例外看起來是 transient（死結在 ShouldRetryOn 的清單
        // 裡），但目前處於使用者自己開的交易中、且沒有設定 EnableRetryOnFailure，沒辦法安全地
        // 自動重試」，於是包成 InvalidOperationException 往外丟、訊息建議去開 EnableRetryOnFailure。
        // 實測 5/5 次死結都是這個形狀：InvalidOperationException -> DbUpdateException ->
        // MySqlException{ErrorCode=LockDeadlock}。所以要攔的型別是 InvalidOperationException，
        // 不是 DbUpdateException——這裡跟下面攔 DuplicateBookingException 的那個 catch 天生就不
        // 會搶到彼此：兩者鎖定的是完全不同的例外型別（InvalidOperationException vs
        // DbUpdateException），沒有共同的父子關係，順序對彼此沒有影響。
        catch (InvalidOperationException ex) when (IsDeadlock(ex))
        {
            throw new TransientConflictException(ex);
        }
        catch (DbUpdateException ex) when (IsDuplicateActivePatientBooking(ex, out var slotId, out var patientId))
        {
            throw new DuplicateBookingException(slotId, patientId, ex);
        }
    }

    public void ResetTracking() => _dbContext.ChangeTracker.Clear();

    /// <summary>
    /// 交易的邊界只在這裡：operation 全部跑完才 Commit。中途任何例外都會讓
    /// transaction 在 await using 的 dispose 時未提交地結束，InnoDB 整筆回滾——
    /// 包含已經成功送出的第一次 SaveChanges（重複預約就是這個情境：UPDATE 成功、
    /// INSERT 撞唯一索引，schedule_slots.booked_count 必須跟著被退回去）。
    /// </summary>
    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var result = await operation(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    /// <summary>
    /// 沿著 InnerException 鏈往下找，只要有任何一層是 MySqlException 且 ErrorCode 是
    /// LockDeadlock（1213）就算數。不能只檢查最外層或只檢查 ex.InnerException 這一層，因為
    /// 這裡的 ex 是最外層的 InvalidOperationException，真正的 MySqlException 在鏈的第三層
    /// （InvalidOperationException -> DbUpdateException -> MySqlException）。
    /// </summary>
    private static bool IsDeadlock(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is MySqlException { ErrorCode: MySqlErrorCode.LockDeadlock })
            {
                return true;
            }
        }

        return false;
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
