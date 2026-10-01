using MediatR;

namespace MedConnect.Application.ScheduleSlots.Queries.GetAvailableSlots;

public sealed record GetAvailableSlotsQuery(long DoctorId, DateOnly Date) : IRequest<List<ScheduleSlotDto>>;
