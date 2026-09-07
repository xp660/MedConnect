namespace MedConnect.Domain.Exceptions;

public sealed class SlotFullException : DomainException
{
    public SlotFullException(long slotId)
        : base($"ScheduleSlot {slotId} has no remaining capacity.")
    {
    }
}
