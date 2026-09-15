using MediatR;

namespace MedConnect.Application.Appointments.Commands.BookAppointment;

public sealed record BookAppointmentCommand(long PatientId, long ScheduleSlotId) : IRequest<BookAppointmentResult>;
