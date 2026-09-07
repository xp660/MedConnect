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

    public void Cancel(DateTimeOffset now)
    {
        if (Status == AppointmentStatus.Cancelled)
        {
            throw new AppointmentAlreadyCancelledException(Id);
        }

        Status = AppointmentStatus.Cancelled;
        CancelledAtUtc = now.UtcDateTime;
    }
}
