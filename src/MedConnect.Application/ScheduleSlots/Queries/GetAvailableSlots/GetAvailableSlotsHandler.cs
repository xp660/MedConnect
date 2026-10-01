using MediatR;
using MedConnect.Application.Abstractions;
using MedConnect.Application.Common.Exceptions;

namespace MedConnect.Application.ScheduleSlots.Queries.GetAvailableSlots;

public sealed class GetAvailableSlotsHandler : IRequestHandler<GetAvailableSlotsQuery, List<ScheduleSlotDto>>
{
    private readonly IDoctorRepository _doctorRepository;
    private readonly IScheduleSlotQueryRepository _scheduleSlotQueryRepository;

    public GetAvailableSlotsHandler(IDoctorRepository doctorRepository, IScheduleSlotQueryRepository scheduleSlotQueryRepository)
    {
        _doctorRepository = doctorRepository;
        _scheduleSlotQueryRepository = scheduleSlotQueryRepository;
    }

    public async Task<List<ScheduleSlotDto>> Handle(GetAvailableSlotsQuery request, CancellationToken cancellationToken)
    {
        // 查無醫生跟「這天沒有診次」刻意分成兩種不同回應（404 vs 空陣列，見
        // architecture-plan.md §0 v1.6），呼叫端才能分辨是「doctorId 打錯」還是「真的沒診次」。
        if (!await _doctorRepository.ExistsAsync(request.DoctorId, cancellationToken))
        {
            throw new DoctorNotFoundException(request.DoctorId);
        }

        return await _scheduleSlotQueryRepository.GetAvailableSlotsAsync(request.DoctorId, request.Date, cancellationToken);
    }
}
