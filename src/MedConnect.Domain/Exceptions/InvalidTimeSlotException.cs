namespace MedConnect.Domain.Exceptions;

public sealed class InvalidTimeSlotException : DomainException
{
    public InvalidTimeSlotException(DateTime startUtc, DateTime endUtc)
        : base($"TimeSlot start ({startUtc:o}) must be before end ({endUtc:o}).")
    {
    }
}
