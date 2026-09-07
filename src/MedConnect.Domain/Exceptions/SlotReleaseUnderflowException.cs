namespace MedConnect.Domain.Exceptions;

public sealed class SlotReleaseUnderflowException : DomainException
{
    public SlotReleaseUnderflowException(long slotId)
        : base($"ScheduleSlot {slotId} has no booked seats to release.")
    {
    }
}
