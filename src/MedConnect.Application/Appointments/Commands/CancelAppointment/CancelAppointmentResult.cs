using MedConnect.Domain.Enums;

namespace MedConnect.Application.Appointments.Commands.CancelAppointment;

public sealed record CancelAppointmentResult(long AppointmentId, AppointmentStatus Status, DateTime CancelledAtUtc);
