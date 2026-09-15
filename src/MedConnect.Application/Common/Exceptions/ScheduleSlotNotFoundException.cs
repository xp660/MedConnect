namespace MedConnect.Application.Common.Exceptions;

public sealed class ScheduleSlotNotFoundException : Exception
{
    public long ScheduleSlotId { get; }

    public ScheduleSlotNotFoundException(long scheduleSlotId)
        : base($"ScheduleSlot {scheduleSlotId} was not found.")
    {
        ScheduleSlotId = scheduleSlotId;
    }
}
