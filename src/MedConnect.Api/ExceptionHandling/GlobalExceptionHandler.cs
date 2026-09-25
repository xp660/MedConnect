using MedConnect.Application.Common.Exceptions;
using MedConnect.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

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
            SlotFullException => (StatusCodes.Status409Conflict, "SLOT_FULL"),
            SlotClosedException => (StatusCodes.Status409Conflict, "SLOT_NOT_BOOKABLE"),
            SlotInPastException => (StatusCodes.Status409Conflict, "SLOT_NOT_BOOKABLE"),
            ConcurrencyConflictException => (StatusCodes.Status409Conflict, "CONCURRENCY_CONFLICT"),
            TransientConflictException => (StatusCodes.Status409Conflict, "TRANSIENT_CONFLICT"),
            DuplicateBookingException => (StatusCodes.Status409Conflict, "DUPLICATE_BOOKING"),
            InvalidCredentialsException => (StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS"),
            _ => (0, string.Empty)
        };

        if (statusCode == 0)
        {
            // 沒有對應到已知的業務例外：交還給預設的例外處理（500），因為這代表真的是未預期的 bug，
            // 不應該被這裡假裝成某種業務錯誤而吞掉（§5.2：非 500 只適用於「預期內的正常事件」）。
            return false;
        }

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = errorCode,
            Detail = exception.Message,
        };
        problemDetails.Extensions["errorCode"] = errorCode;
        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
