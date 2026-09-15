using MedConnect.Domain.Entities;

namespace MedConnect.Application.Abstractions;

public interface IAppointmentRepository
{
    void Add(Appointment appointment);
}
