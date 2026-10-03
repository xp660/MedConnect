using MedConnect.Application.Common.Validation;

namespace MedConnect.Application.Appointments.Commands.CancelAppointment;

/// <summary>
/// PatientId 來自 JWT，不是使用者輸入，不驗證。Field 名稱用 route 參數名（id）。
/// </summary>
public sealed class CancelAppointmentCommandValidator : IRequestValidator<CancelAppointmentCommand>
{
    public IReadOnlyList<ValidationError> Validate(CancelAppointmentCommand request)
    {
        var errors = new List<ValidationError>();

        if (request.AppointmentId <= 0)
        {
            errors.Add(new ValidationError("id", "must be greater than 0"));
        }

        return errors;
    }
}
