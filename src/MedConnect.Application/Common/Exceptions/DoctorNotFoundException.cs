namespace MedConnect.Application.Common.Exceptions;

public sealed class DoctorNotFoundException : Exception
{
    public long DoctorId { get; }

    public DoctorNotFoundException(long doctorId)
        : base($"Doctor {doctorId} was not found.")
    {
        DoctorId = doctorId;
    }
}
