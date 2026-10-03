using MediatR;

namespace MedConnect.Application.Appointments.Commands.CancelAppointment;

public sealed record CancelAppointmentCommand(long AppointmentId, long PatientId) : IRequest<CancelAppointmentResult>;
