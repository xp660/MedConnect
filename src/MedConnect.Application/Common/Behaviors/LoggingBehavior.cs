using MediatR;
using Microsoft.Extensions.Logging;

namespace MedConnect.Application.Common.Behaviors;

/// <summary>
/// architecture-plan.md §5.1 / §0 v1.12：註冊在最外層（Logging → Validation → Retry），
/// 每個請求恰好留一行：請求型別名稱、耗時、結果。
///
/// 刻意的限制（不是還沒做）：
///   1. 絕不記請求內容，也不序列化整個 Command。LoginCommand 帶著明文密碼，把 request 丟給 logger
///      等於把密碼寫進日誌；「記個別欄位就好」之所以也不做，是因為這個 behavior 是泛型的、
///      不知道哪個欄位敏感，逐欄位白名單只會變成下一個人忘記更新的清單。需要的業務欄位
///      （AppointmentId、SlotId…）由 Handler 在有意義的地方明確記。
///   2. 遇到例外只記「結果是某型別的例外」，然後原樣重新丟出，不用 Error 層級、也不傳例外物件。
///      例外的層級只能在 GlobalExceptionHandler 一處決定，否則同一個例外會被記兩次。
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const string SuccessOutcome = "Success";

    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    private readonly TimeProvider _timeProvider;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger, TimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var startTimestamp = _timeProvider.GetTimestamp();

        try
        {
            var response = await next(cancellationToken);

            Log(startTimestamp, SuccessOutcome);

            return response;
        }
        catch (Exception ex)
        {
            Log(startTimestamp, ex.GetType().Name);

            throw;
        }
    }

    private void Log(long startTimestamp, string outcome) =>
        _logger.LogInformation(
            "Handled {RequestName} in {ElapsedMs} ms with {Outcome}",
            typeof(TRequest).Name,
            _timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds,
            outcome);
}
