using MedConnect.Domain.Enums;
using MedConnect.Domain.Exceptions;
using MedConnect.Domain.ValueObjects;

namespace MedConnect.Domain.Entities;

public class ScheduleSlot
{
    public long Id { get; private set; }
    public long DoctorId { get; private set; }
    public TimeSlot TimeSlot { get; private set; } = null!;
    public int Capacity { get; private set; }
    public int BookedCount { get; private set; }
    public SlotStatus Status { get; private set; }
    public int Version { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public int AvailableCount => Capacity - BookedCount;

    private ScheduleSlot()
    {
        // EF Core
    }

    public ScheduleSlot(long doctorId, TimeSlot timeSlot, int capacity, DateTimeOffset now)
    {
        if (capacity < 1)
        {
            throw new InvalidCapacityException(capacity);
        }

        DoctorId = doctorId;
        TimeSlot = timeSlot;
        Capacity = capacity;
        BookedCount = 0;
        Status = SlotStatus.Open;
        CreatedAtUtc = now.UtcDateTime;
        UpdatedAtUtc = now.UtcDateTime;
    }

    public void Book(DateTimeOffset now)
    {
        if (Status != SlotStatus.Open)
        {
            throw new SlotClosedException(Id);
        }

        if (TimeSlot.StartUtc <= now.UtcDateTime)
        {
            throw new SlotInPastException(Id);
        }

        if (BookedCount >= Capacity)
        {
            throw new SlotFullException(Id);
        }

        BookedCount++;
        UpdatedAtUtc = now.UtcDateTime;
    }

    // 目前沒有任何 Application/Api 呼叫端（MVP 沒有 Admin 關閉時段的 endpoint，見 architecture-plan.md §12
    // 第 15 點）。保留這個方法是因為 Book() 本身就以 Status == Open 為前提，Status 若無法被設成別的值，
    // 該分支永遠無法被真實情境觸發，只能靠 reflection 這類手段測試，故先補上這個最小的公開轉換方法。
    public void Close(DateTimeOffset now)
    {
        Status = SlotStatus.Closed;
        UpdatedAtUtc = now.UtcDateTime;
    }

    public void Release(DateTimeOffset now)
    {
        if (BookedCount <= 0)
        {
            throw new SlotReleaseUnderflowException(Id);
        }

        BookedCount--;
        UpdatedAtUtc = now.UtcDateTime;
    }
}
