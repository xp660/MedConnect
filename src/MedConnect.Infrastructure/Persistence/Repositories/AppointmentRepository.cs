using MedConnect.Application.Abstractions;
using MedConnect.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedConnect.Infrastructure.Persistence.Repositories;

public sealed class AppointmentRepository : IAppointmentRepository
{
    private readonly MedConnectDbContext _dbContext;

    public AppointmentRepository(MedConnectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Appointment?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        return _dbContext.Appointments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public void Add(Appointment appointment)
    {
        _dbContext.Appointments.Add(appointment);
    }
}
