using MedConnect.Domain.Enums;
using MedConnect.Domain.Exceptions;

namespace MedConnect.Domain.Entities;

public class Appointment
{
    public long Id { get; private set; }
    public long SlotId { get; private set; }
    public long PatientId { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public DateTime BookedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public int Version { get; private set; }

    private Appointment()
    {
        // EF Core
    }

    public Appointment(long slotId, long patientId, DateTimeOffset now)
    {
        SlotId = slotId;
        PatientId = patientId;
        Status = AppointmentStatus.Booked;
        BookedAtUtc = now.UtcDateTime;
    }

    // 取消流程必須先寫 ScheduleSlot（Release）再寫 Appointment（Cancel），所以「已取消」這條規則
    // 得在 Release() 之前就能單獨驗證；否則對 BookedCount 已為 0 的時段重複取消，會先撞上
    // SlotReleaseUnderflowException（未對映，500），而不是 AppointmentAlreadyCancelledException（409）。
    // Cancel() 也呼叫它，規則只活在這一個地方。
    public void EnsureCanBeCancelled()
    {
        if (Status == AppointmentStatus.Cancelled)
        {
            throw new AppointmentAlreadyCancelledException(Id);
        }
    }

    public void Cancel(DateTimeOffset now)
    {
        EnsureCanBeCancelled();

        Status = AppointmentStatus.Cancelled;
        CancelledAtUtc = now.UtcDateTime;
    }
}
