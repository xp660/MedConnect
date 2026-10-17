using MedConnect.Application.Common.Exceptions;
using MedConnect.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace MedConnect.Api.ExceptionHandling;

/// <summary>
/// architecture-plan.md §4.4：Controller 不做業務例外的 try/catch，例外對應集中在這一個地方。
/// §5.2/§7.3 的 errorCode 對照表就是這裡實作的依據。
///
/// 這裡同時是「例外該用什麼日誌層級」唯一決定的地方（§0 v1.12）：每個例外恰好記一筆。
/// 已對映的是預期內的正常事件，不傳例外物件、沒有堆疊；沒對映的才是 bug，用 Error 帶完整例外與堆疊。
/// ASP.NET 內建的 ExceptionHandlerMiddleware 與 EF Core 對同一個例外各自也會記 Error，那幾筆
/// 在 Serilog 設定（ApiLogging）以事件 Id 精確過濾掉，否則同一個例外會重複記、
/// 而且「正常的 409」會出現 Error 層級的日誌。
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, errorCode, logLevel) = exception switch
        {
            ScheduleSlotNotFoundException => (StatusCodes.Status404NotFound, "SLOT_NOT_FOUND", LogLevel.Information),
            DoctorNotFoundException => (StatusCodes.Status404NotFound, "DOCTOR_NOT_FOUND", LogLevel.Information),
            AppointmentNotFoundException => (StatusCodes.Status404NotFound, "APPOINTMENT_NOT_FOUND", LogLevel.Information),
            AppointmentAlreadyCancelledException => (StatusCodes.Status409Conflict, "ALREADY_CANCELLED", LogLevel.Information),
            SlotFullException => (StatusCodes.Status409Conflict, "SLOT_FULL", LogLevel.Information),
            SlotClosedException => (StatusCodes.Status409Conflict, "SLOT_NOT_BOOKABLE", LogLevel.Information),
            SlotInPastException => (StatusCodes.Status409Conflict, "SLOT_NOT_BOOKABLE", LogLevel.Information),
            // 單次衝突（沒有 RetryBehavior 吸收的情況）：樂觀鎖的正常結果。
            ConcurrencyConflictException => (StatusCodes.Status409Conflict, "CONCURRENCY_CONFLICT", LogLevel.Information),
            // 死結與重試用盡：系統已經撐不住了，值得有人看到，所以是 Warning。
            TransientConflictException => (StatusCodes.Status409Conflict, "TRANSIENT_CONFLICT", LogLevel.Warning),
            RetryExhaustedException => (StatusCodes.Status409Conflict, "RETRY_EXHAUSTED", LogLevel.Warning),
            DuplicateBookingException => (StatusCodes.Status409Conflict, "DUPLICATE_BOOKING", LogLevel.Information),
            InvalidCredentialsException => (StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS", LogLevel.Information),
            RequestValidationException => (StatusCodes.Status400BadRequest, ApiProblemDetails.ValidationFailedErrorCode, LogLevel.Information),
            _ => (0, string.Empty, LogLevel.None)
        };

        if (statusCode == 0)
        {
            // 沒有對應到已知的業務例外：這代表真的是未預期的 bug，不應該被這裡假裝成某種業務錯誤而吞掉
            // （§5.2：非 500 只適用於「預期內的正常事件」）。完整例外與堆疊在這裡記錄恰好一次，
            // 然後交還給預設的例外處理（500）。內建 middleware 自己那一筆被過濾掉了，所以 500 的紀錄
            // 全靠這一行——測試（UnhandledExceptionLoggingTests）守著它不會悄悄消失。
            _logger.LogError(exception, "Unhandled exception of type {ExceptionType}", exception.GetType().Name);

            return false;
        }

        LogMappedException(logLevel, exception, statusCode, errorCode);

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

    /// <summary>
    /// 刻意不把例外物件傳給 logger：已對映的例外是預期內事件，堆疊只是雜訊；例外訊息也可能帶使用者輸入。
    /// 只記 errorCode、HTTP 狀態與例外型別名稱，需要的話（重試用盡）再加嘗試次數與最後一次失敗的型別。
    /// </summary>
    private void LogMappedException(LogLevel logLevel, Exception exception, int statusCode, string errorCode)
    {
        if (exception is RetryExhaustedException retryExhausted)
        {
            _logger.Log(
                logLevel,
                "Request failed with {ErrorCode} ({StatusCode}) after {Attempts} attempts, last failure {LastFailureType}",
                errorCode, statusCode, retryExhausted.Attempts, retryExhausted.InnerException?.GetType().Name);

            return;
        }

        _logger.Log(
            logLevel,
            "Request failed with {ErrorCode} ({StatusCode}) due to {ExceptionType}",
            errorCode, statusCode, exception.GetType().Name);
    }
}
