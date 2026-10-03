using MedConnect.Domain.Entities;

namespace MedConnect.Application.Abstractions;

public interface IAppointmentRepository
{
    Task<Appointment?> GetByIdAsync(long id, CancellationToken cancellationToken);

    void Add(Appointment appointment);
}
