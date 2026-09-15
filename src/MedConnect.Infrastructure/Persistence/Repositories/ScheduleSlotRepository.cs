using MedConnect.Application.Abstractions;
using MedConnect.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedConnect.Infrastructure.Persistence.Repositories;

public sealed class ScheduleSlotRepository : IScheduleSlotRepository
{
    private readonly MedConnectDbContext _dbContext;

    public ScheduleSlotRepository(MedConnectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<ScheduleSlot?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        return _dbContext.ScheduleSlots.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public void Update(ScheduleSlot slot)
    {
        // slot 是透過這個同一個 DbContext 的 GetByIdAsync 載入的，本來就已被追蹤，
        // 這裡呼叫 Update() 只是讓「打算持久化這個變更」的意圖明確寫出來，行為上是安全的 no-op。
        _dbContext.ScheduleSlots.Update(slot);
    }
}
