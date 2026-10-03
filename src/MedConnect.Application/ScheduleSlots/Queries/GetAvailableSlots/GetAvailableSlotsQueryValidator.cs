using MedConnect.Application.Common.Validation;

namespace MedConnect.Application.ScheduleSlots.Queries.GetAvailableSlots;

public sealed class GetAvailableSlotsQueryValidator : IRequestValidator<GetAvailableSlotsQuery>
{
    public IReadOnlyList<ValidationError> Validate(GetAvailableSlotsQuery request)
    {
        var errors = new List<ValidationError>();

        if (request.DoctorId <= 0)
        {
            errors.Add(new ValidationError("doctorId", "must be greater than 0"));
        }

        // query string 少了 date 時，model binding 不會報錯，而是悄悄給 default(DateOnly) = 0001-01-01，
        // 然後整個流程照跑、回 200 空陣列（已用 characterization 測試實測確認）。
        // 所以 default 值在這裡等同「沒帶」。
        if (request.Date == default)
        {
            errors.Add(new ValidationError("date", "is required"));
        }

        return errors;
    }
}
