using MedConnect.Domain.Entities;

namespace MedConnect.Application.Abstractions;

public interface IPatientRepository
{
    Task<Patient?> GetByUserIdAsync(long userId, CancellationToken cancellationToken);
}
