using MediatR;
using MedConnect.Application.Abstractions;
using MedConnect.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace MedConnect.Application.Common.Behaviors;

/// <summary>
/// architecture-plan.md §12 第 7 項 / §0 v1.9：只對「重讀最新資料後有機會成功」的兩種衝突重試。
///
/// 只重試：
///   - ConcurrencyConflictException（樂觀鎖 version 被別人搶先）
///   - TransientConflictException（InnoDB 死結，DB 主動犧牲這個交易）
/// 絕不重試 SlotFullException / DuplicateBookingException / AppointmentAlreadyCancelledException 等
/// 業務上已確定的結果：重試只會得到同樣的答案。重試後若重新讀到「已滿」，會自然得到 SLOT_FULL，
/// 這正是我們要的行為。
///
/// 位置：註冊在 ValidationBehavior 之後（內層）。不合法的輸入在外層就被擋下，不會進入這個迴圈。
/// 每次 next() 都會重新跑完整個 Handler（含它自己開的顯式交易），所以每次嘗試都是新的交易。
///
/// 重試前必須 ResetTracking()：這個 behavior 與 Handler 共用同一個 Scoped DbContext，不清掉的話
/// change tracker 會把第一次的舊實體原樣回傳，同樣的衝突會重演。
/// </summary>
public sealed class RetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly RetryOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RetryBehavior<TRequest, TResponse>> _logger;

    public RetryBehavior(
        IUnitOfWork unitOfWork,
        RetryOptions options,
        TimeProvider timeProvider,
        ILogger<RetryBehavior<TRequest, TResponse>> logger)
    {
        _unitOfWork = unitOfWork;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await next(cancellationToken);
            }
            catch (Exception ex) when (ex is ConcurrencyConflictException or TransientConflictException)
            {
                if (attempt >= _options.MaxRetries)
                {
                    throw new RetryExhaustedException(attempt + 1, ex);
                }

                // 層級依失敗原因而不同（architecture-plan.md §0 v1.12 第 14 項）：
                //   ConcurrencyConflictException → Debug：樂觀鎖下衝突是預期內的正常行為，50 人搶 5 個名額會有上百行，
                //     放在 Information 以上會淹沒真正需要看的訊息。
                //   TransientConflictException（死結）→ Warning：Booking 與 Cancel 的鎖定順序刻意一致就是為了不死結，
                //     所以每一次死結都代表鎖定順序或競爭出了預期之外的事；即使被重試吸收、請求最後成功，也值得被看到。
                // 重試用盡另由 GlobalExceptionHandler 以 Warning 記一次。
                var retryLevel = ex is TransientConflictException ? LogLevel.Warning : LogLevel.Debug;
                _logger.Log(
                    retryLevel,
                    "Retrying {RequestName} after {FailureType} (retry {Retry} of {MaxRetries})",
                    typeof(TRequest).Name, ex.GetType().Name, attempt + 1, _options.MaxRetries);

                await Task.Delay(_options.GetDelay(attempt, Random.Shared), _timeProvider, cancellationToken);

                _unitOfWork.ResetTracking();
            }
        }
    }
}
