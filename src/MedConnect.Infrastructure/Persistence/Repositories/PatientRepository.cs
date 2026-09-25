using MedConnect.Application.Abstractions;
using MedConnect.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedConnect.Infrastructure.Persistence.Repositories;

public sealed class PatientRepository : IPatientRepository
{
    private readonly MedConnectDbContext _dbContext;

    public PatientRepository(MedConnectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Patient?> GetByUserIdAsync(long userId, CancellationToken cancellationToken) =>
        _dbContext.Patients.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
}
