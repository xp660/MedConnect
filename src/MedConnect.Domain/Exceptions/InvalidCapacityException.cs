namespace MedConnect.Domain.Exceptions;

public sealed class InvalidCapacityException : DomainException
{
    public InvalidCapacityException(int capacity)
        : base($"Capacity must be at least 1, but was {capacity}.")
    {
    }
}
