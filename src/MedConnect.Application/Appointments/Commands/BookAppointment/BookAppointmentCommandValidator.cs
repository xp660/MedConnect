using MedConnect.Application.Common.Validation;

namespace MedConnect.Application.Appointments.Commands.BookAppointment;

/// <summary>
/// PatientId 刻意不驗證：它來自已驗證的 JWT claim，不是使用者輸入。
/// Field 名稱用對外的 request body 欄位名（slotId），不是命令的屬性名（ScheduleSlotId）。
/// </summary>
public sealed class BookAppointmentCommandValidator : IRequestValidator<BookAppointmentCommand>
{
    public IReadOnlyList<ValidationError> Validate(BookAppointmentCommand request)
    {
        var errors = new List<ValidationError>();

        if (request.ScheduleSlotId <= 0)
        {
            errors.Add(new ValidationError("slotId", "must be greater than 0"));
        }

        return errors;
    }
}
