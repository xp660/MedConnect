using MedConnect.Domain.Entities;

namespace MedConnect.Application.Abstractions;

public interface IScheduleSlotRepository
{
    Task<ScheduleSlot?> GetByIdAsync(long id, CancellationToken cancellationToken);

    void Update(ScheduleSlot slot);
}
