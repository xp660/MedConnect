namespace MedConnect.Application.Abstractions;

public interface IDoctorRepository
{
    Task<bool> ExistsAsync(long doctorId, CancellationToken cancellationToken);
}
