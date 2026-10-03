using MediatR;
using MedConnect.Application.Abstractions;
using MedConnect.Application.Common.Exceptions;

namespace MedConnect.Application.Appointments.Commands.CancelAppointment;

public sealed class CancelAppointmentHandler : IRequestHandler<CancelAppointmentCommand, CancelAppointmentResult>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IScheduleSlotRepository _scheduleSlotRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public CancelAppointmentHandler(
        IAppointmentRepository appointmentRepository,
        IScheduleSlotRepository scheduleSlotRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _appointmentRepository = appointmentRepository;
        _scheduleSlotRepository = scheduleSlotRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<CancelAppointmentResult> Handle(CancelAppointmentCommand request, CancellationToken cancellationToken)
    {
        var appointment = await _appointmentRepository.GetByIdAsync(request.AppointmentId, cancellationToken);

        // 「不存在」與「不是你的」走同一條路徑、丟同一個例外（見 AppointmentNotFoundException），
        // 防止用 appointmentId 枚舉別人的預約。這個檢查必須排在下面的「已取消」檢查之前：
        // 否則陌生人可以從 409 vs 404 的差異推測出別人預約的取消狀態。
        if (appointment is null || appointment.PatientId != request.PatientId)
        {
            throw new AppointmentNotFoundException(request.AppointmentId);
        }

        // 預約存在、卻找不到它的時段，是違反 FK 的資料不一致 bug，不是呼叫端可以處理的「找不到」，
        // 所以刻意不丟 ScheduleSlotNotFoundException（那會對映成誤導人的 404 SLOT_NOT_FOUND），
        // 讓它以未對映例外外洩成 500。
        var slot = await _scheduleSlotRepository.GetByIdAsync(appointment.SlotId, cancellationToken)
            ?? throw new InvalidOperationException($"Appointment {appointment.Id} references missing ScheduleSlot {appointment.SlotId}.");

        // 必須在 slot.Release() 之前先驗證「已取消」：Release() 對 BookedCount 已為 0 的時段會先丟
        // SlotReleaseUnderflowException（未對映，500），重複取消就會變成 500 而不是 409 ALREADY_CANCELLED。
        // 這個檢查基於當下讀到的快照，不是併發守衛；真正的守衛是下面兩次 flush 各自的 version 檢查。
        appointment.EnsureCanBeCancelled();

        var now = _timeProvider.GetUtcNow();

        return await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            // 順序跟 BookAppointmentHandler 一致：先 UPDATE schedule_slots（拿 X 鎖），再 UPDATE appointments。
            // 兩次 SaveChanges 不可合併：EF 在同一次 flush 內按資料表名稱字典序送出語句，
            // appointments 會排在 schedule_slots 前面，鎖定順序就會跟 Booking 相反。
            //
            // appointment.Cancel() 也必須留在第一次 flush 之後才呼叫：change tracker 會把所有已修改的
            // 實體一起 flush，提早呼叫會讓 appointment 的 UPDATE 被併進第一次 SaveChanges。
            slot.Release(now);
            _scheduleSlotRepository.Update(slot);
            await _unitOfWork.SaveChangesAsync(ct);

            appointment.Cancel(now);
            await _unitOfWork.SaveChangesAsync(ct);

            return new CancelAppointmentResult(appointment.Id, appointment.Status, appointment.CancelledAtUtc!.Value);
        }, cancellationToken);
    }
}
