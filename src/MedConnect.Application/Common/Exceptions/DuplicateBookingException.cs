namespace MedConnect.Application.Common.Exceptions;

/// <summary>
/// Infrastructure 層在 SaveChanges 攔截到 ux_appt_slot_active_patient 唯一索引衝突時轉譯成這個型別，
/// 與 ConcurrencyConflictException 是兩種不同來源的衝突，不可合併處理（architecture-plan.md §5.2）。不可重試。
/// </summary>
public sealed class DuplicateBookingException : Exception
{
    public long ScheduleSlotId { get; }
    public long PatientId { get; }

    public DuplicateBookingException(long scheduleSlotId, long patientId, Exception innerException)
        : base($"Patient {patientId} already has an active booking for schedule slot {scheduleSlotId}.", innerException)
    {
        ScheduleSlotId = scheduleSlotId;
        PatientId = patientId;
    }
}
