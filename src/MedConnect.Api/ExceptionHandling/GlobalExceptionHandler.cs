using MedConnect.Application.Common.Exceptions;
using MedConnect.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace MedConnect.Api.ExceptionHandling;

/// <summary>
/// architecture-plan.md §4.4：Controller 不做業務例外的 try/catch，例外對應集中在這一個地方。
/// §5.2/§7.3 的 errorCode 對照表就是這裡實作的依據。
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, errorCode) = exception switch
        {
            ScheduleSlotNotFoundException => (StatusCodes.Status404NotFound, "SLOT_NOT_FOUND"),
            DoctorNotFoundException => (StatusCodes.Status404NotFound, "DOCTOR_NOT_FOUND"),
            AppointmentNotFoundException => (StatusCodes.Status404NotFound, "APPOINTMENT_NOT_FOUND"),
            AppointmentAlreadyCancelledException => (StatusCodes.Status409Conflict, "ALREADY_CANCELLED"),
            SlotFullException => (StatusCodes.Status409Conflict, "SLOT_FULL"),
            SlotClosedException => (StatusCodes.Status409Conflict, "SLOT_NOT_BOOKABLE"),
            SlotInPastException => (StatusCodes.Status409Conflict, "SLOT_NOT_BOOKABLE"),
            ConcurrencyConflictException => (StatusCodes.Status409Conflict, "CONCURRENCY_CONFLICT"),
            TransientConflictException => (StatusCodes.Status409Conflict, "TRANSIENT_CONFLICT"),
            DuplicateBookingException => (StatusCodes.Status409Conflict, "DUPLICATE_BOOKING"),
            InvalidCredentialsException => (StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS"),
            RequestValidationException => (StatusCodes.Status400BadRequest, ApiProblemDetails.ValidationFailedErrorCode),
            _ => (0, string.Empty)
        };

        if (statusCode == 0)
        {
            // 沒有對應到已知的業務例外：交還給預設的例外處理（500），因為這代表真的是未預期的 bug，
            // 不應該被這裡假裝成某種業務錯誤而吞掉（§5.2：非 500 只適用於「預期內的正常事件」）。
            return false;
        }

        var problemDetails = exception is RequestValidationException validationException
            ? ApiProblemDetails.CreateValidationFailed(httpContext, statusCode, ApiProblemDetails.ToErrors(validationException.Errors))
            : ApiProblemDetails.Create(httpContext, statusCode, errorCode, exception.Message);

        httpContext.Response.StatusCode = statusCode;

        // contentType 必須直接傳給 WriteAsJsonAsync：它會無條件把 Content-Type 覆寫成 application/json，
        // 先設 Response.ContentType 沒有用（本檔案舊版就是這樣，實際回的一直是 application/json，
        // 與 §7 規定的 application/problem+json 不符）。
        await httpContext.Response.WriteAsJsonAsync(
            problemDetails, options: null, contentType: ApiProblemDetails.ContentType, cancellationToken);

        return true;
    }
}
