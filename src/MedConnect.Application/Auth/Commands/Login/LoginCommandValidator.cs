using MedConnect.Application.Common.Validation;

namespace MedConnect.Application.Auth.Commands.Login;

/// <summary>
/// 只檢查結構，刻意不檢查 Email 格式與帳號是否存在：這裡的 400 只反映「輸入形狀不對」，
/// 跟「這個帳號存不存在」完全無關，不會成為 User Enumeration 的管道（architecture-plan.md §7.1）。
/// </summary>
public sealed class LoginCommandValidator : IRequestValidator<LoginCommand>
{
    // users.email 欄位是 VARCHAR(256)（architecture-plan.md §6.1）。
    private const int EmailMaxLength = 256;

    public IReadOnlyList<ValidationError> Validate(LoginCommand request)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors.Add(new ValidationError("email", "is required"));
        }
        else if (request.Email.Length > EmailMaxLength)
        {
            errors.Add(new ValidationError("email", $"must be at most {EmailMaxLength} characters"));
        }

        // 只擋空字串，不 Trim：密碼本來就可以以空白開頭或結尾。
        if (string.IsNullOrEmpty(request.Password))
        {
            errors.Add(new ValidationError("password", "is required"));
        }

        return errors;
    }
}
