using MediatR;
using MedConnect.Application.Abstractions;
using MedConnect.Application.Common.Exceptions;
using MedConnect.Domain.Entities;

namespace MedConnect.Application.Appointments.Commands.BookAppointment;

public sealed class BookAppointmentHandler : IRequestHandler<BookAppointmentCommand, BookAppointmentResult>
{
    private readonly IScheduleSlotRepository _scheduleSlotRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public BookAppointmentHandler(
        IScheduleSlotRepository scheduleSlotRepository,
        IAppointmentRepository appointmentRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _scheduleSlotRepository = scheduleSlotRepository;
        _appointmentRepository = appointmentRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<BookAppointmentResult> Handle(BookAppointmentCommand request, CancellationToken cancellationToken)
    {
        var slot = await _scheduleSlotRepository.GetByIdAsync(request.ScheduleSlotId, cancellationToken)
            ?? throw new ScheduleSlotNotFoundException(request.ScheduleSlotId);

        var now = _timeProvider.GetUtcNow();

        // Domain 檢查基於這個當下讀到的快照，只是第一道防線；真正的併發守衛在
        // IUnitOfWork.SaveChangesAsync() 底下 UPDATE ... WHERE version = ? 的那一步（見 §5.2）。
        slot.Book(now);
        _scheduleSlotRepository.Update(slot);

        var appointment = new Appointment(slot.Id, request.PatientId, now);
        _appointmentRepository.Add(appointment);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new BookAppointmentResult(appointment.Id, slot.Id, appointment.PatientId, appointment.Status, appointment.BookedAtUtc);
    }
}
