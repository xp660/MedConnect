using MedConnect.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MedConnect.Infrastructure.Persistence.Repositories;

public sealed class DoctorRepository : IDoctorRepository
{
    private readonly MedConnectDbContext _dbContext;

    public DoctorRepository(MedConnectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> ExistsAsync(long doctorId, CancellationToken cancellationToken)
    {
        return _dbContext.Doctors.AsNoTracking().AnyAsync(d => d.Id == doctorId, cancellationToken);
    }
}
