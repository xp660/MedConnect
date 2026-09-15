using MedConnect.Domain.Enums;

namespace MedConnect.Application.Appointments.Commands.BookAppointment;

public sealed record BookAppointmentResult(
    long AppointmentId,
    long ScheduleSlotId,
    long PatientId,
    AppointmentStatus Status,
    DateTime BookedAtUtc);
