using MediatR;
using MedConnect.Application.Abstractions;
using MedConnect.Application.Common.Exceptions;
using MedConnect.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MedConnect.Application.Appointments.Commands.BookAppointment;

public sealed class BookAppointmentHandler : IRequestHandler<BookAppointmentCommand, BookAppointmentResult>
{
    private readonly IScheduleSlotRepository _scheduleSlotRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BookAppointmentHandler> _logger;

    public BookAppointmentHandler(
        IScheduleSlotRepository scheduleSlotRepository,
        IAppointmentRepository appointmentRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<BookAppointmentHandler> logger)
    {
        _scheduleSlotRepository = scheduleSlotRepository;
        _appointmentRepository = appointmentRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<BookAppointmentResult> Handle(BookAppointmentCommand request, CancellationToken cancellationToken)
    {
        var slot = await _scheduleSlotRepository.GetByIdAsync(request.ScheduleSlotId, cancellationToken)
            ?? throw new ScheduleSlotNotFoundException(request.ScheduleSlotId);

        var now = _timeProvider.GetUtcNow();

        // Domain 檢查基於這個當下讀到的快照，只是第一道防線；真正的併發守衛在
        // IUnitOfWork.SaveChangesAsync() 底下 UPDATE ... WHERE version = ? 的那一步（見 §5.2）。
        // 這一步刻意留在交易外面：時段已滿/已關閉/已過期都應該連交易都不用開就直接拒絕。
        slot.Book(now);

        var result = await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            // 順序是有意義的，不能對調（見 IUnitOfWork.ExecuteInTransactionAsync 的說明）：
            // 先讓 schedule_slots 的 UPDATE 拿到 X 鎖，appointments 的 INSERT 才做 FK 檢查。
            // 反過來的話，FK 檢查的 S 鎖會和 UPDATE 的 X 鎖互相升級，50 個併發請求會死結。
            _scheduleSlotRepository.Update(slot);
            await _unitOfWork.SaveChangesAsync(ct);

            var appointment = new Appointment(slot.Id, request.PatientId, now);
            _appointmentRepository.Add(appointment);
            await _unitOfWork.SaveChangesAsync(ct);

            return new BookAppointmentResult(
                appointment.Id, slot.Id, appointment.PatientId, appointment.Status, appointment.BookedAtUtc);
        }, cancellationToken);

        // 業務事件：只記 ID。放在交易 commit 之後，所以記到的一定是真的成功、而且只在成功的那次嘗試記一次。
        _logger.LogInformation(
            "Appointment {AppointmentId} booked for slot {SlotId} by patient {PatientId}",
            result.AppointmentId, slot.Id, result.PatientId);

        return result;
    }
}
