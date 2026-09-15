using MedConnect.Application.Abstractions;
using MedConnect.Domain.Entities;

namespace MedConnect.Infrastructure.Persistence.Repositories;

public sealed class AppointmentRepository : IAppointmentRepository
{
    private readonly MedConnectDbContext _dbContext;

    public AppointmentRepository(MedConnectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Add(Appointment appointment)
    {
        _dbContext.Appointments.Add(appointment);
    }
}
