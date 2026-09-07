namespace MedConnect.Domain.Exceptions;

public sealed class SlotInPastException : DomainException
{
    public SlotInPastException(long slotId)
        : base($"ScheduleSlot {slotId} has already started and cannot be booked.")
    {
    }
}
