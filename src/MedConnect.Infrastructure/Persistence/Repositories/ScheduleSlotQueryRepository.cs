using MedConnect.Application.Abstractions;
using MedConnect.Application.ScheduleSlots.Queries.GetAvailableSlots;
using MedConnect.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MedConnect.Infrastructure.Persistence.Repositories;

public sealed class ScheduleSlotQueryRepository : IScheduleSlotQueryRepository
{
    private readonly MedConnectDbContext _dbContext;

    public ScheduleSlotQueryRepository(MedConnectDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ScheduleSlotDto>> GetAvailableSlotsAsync(long doctorId, DateOnly date, CancellationToken cancellationToken)
    {
        var dayStartUtc = date.ToDateTime(TimeOnly.MinValue);
        var dayEndUtc = dayStartUtc.AddDays(1);

        // 先在 DB 端把篩選、投影都做完（AsNoTracking + Select 只挑需要的欄位，不載入整個
        // ScheduleSlot Aggregate），再把 DateTime -> DateTimeOffset、enum -> string 這類
        // 不可轉譯成 SQL 的轉換留到記憶體內的第二段 Select 做，避免依賴 EF Core 對「最終投影
        // 允許 client evaluation」這個行為細節。
        var rows = await _dbContext.ScheduleSlots
            .AsNoTracking()
            .Where(s => s.DoctorId == doctorId
                && s.Status == SlotStatus.Open
                && s.TimeSlot.StartUtc >= dayStartUtc
                && s.TimeSlot.StartUtc < dayEndUtc)
            .Select(s => new
            {
                s.Id,
                s.DoctorId,
                StartUtc = s.TimeSlot.StartUtc,
                s.Capacity,
                s.BookedCount,
                s.Status,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new ScheduleSlotDto(
                r.Id,
                r.DoctorId,
                new DateTimeOffset(DateTime.SpecifyKind(r.StartUtc, DateTimeKind.Utc)),
                r.Capacity,
                r.Capacity - r.BookedCount,
                r.Status.ToString()))
            .ToList();
    }
}
