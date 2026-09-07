using MedConnect.Domain.Exceptions;

namespace MedConnect.Domain.ValueObjects;

public sealed record TimeSlot
{
    public DateTime StartUtc { get; }
    public DateTime EndUtc { get; }

    public TimeSlot(DateTime startUtc, DateTime endUtc)
    {
        if (startUtc >= endUtc)
        {
            throw new InvalidTimeSlotException(startUtc, endUtc);
        }

        StartUtc = startUtc;
        EndUtc = endUtc;
    }
}
