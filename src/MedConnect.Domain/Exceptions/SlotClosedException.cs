namespace MedConnect.Domain.Exceptions;

public sealed class SlotClosedException : DomainException
{
    public SlotClosedException(long slotId)
        : base($"ScheduleSlot {slotId} is closed and cannot be booked.")
    {
    }
}
