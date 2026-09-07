namespace MedConnect.Domain.Exceptions;

public sealed class AppointmentAlreadyCancelledException : DomainException
{
    public AppointmentAlreadyCancelledException(long appointmentId)
        : base($"Appointment {appointmentId} is already cancelled.")
    {
    }
}
