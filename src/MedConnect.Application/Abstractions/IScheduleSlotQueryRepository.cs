using MedConnect.Application.ScheduleSlots.Queries.GetAvailableSlots;

namespace MedConnect.Application.Abstractions;

/// <summary>
/// 跟既有的 IScheduleSlotRepository（寫入用，操作 ScheduleSlot Aggregate）刻意分開：
/// 這裡回傳 DTO 而非 Domain Entity，讓查詢路徑不必載入完整 Aggregate，同時 Application
/// 仍然不直接依賴 EF Core（見 architecture-plan.md §0 v1.6 對 §4.2 Deferred Decision 的落地）。
/// </summary>
public interface IScheduleSlotQueryRepository
{
    Task<List<ScheduleSlotDto>> GetAvailableSlotsAsync(long doctorId, DateOnly date, CancellationToken cancellationToken);
}
