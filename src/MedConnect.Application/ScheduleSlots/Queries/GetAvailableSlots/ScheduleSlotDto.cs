namespace MedConnect.Application.ScheduleSlots.Queries.GetAvailableSlots;

public sealed record ScheduleSlotDto(long Id, long DoctorId, DateTimeOffset StartUtc, int Capacity, int AvailableCount, string Status);
