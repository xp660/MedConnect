using MediatR;
using MedConnect.Application.Common.Exceptions;
using MedConnect.Application.Common.Validation;

namespace MedConnect.Application.Common.Behaviors;

/// <summary>
/// architecture-plan.md §5.1 步驟 ④ 的 Validation：在 Handler 之前跑完所有該 request 的 validator，
/// 有任何錯誤就丟 RequestValidationException（由 GlobalExceptionHandler 對映成 400 VALIDATION_FAILED），
/// Handler 完全不會被呼叫，也就不會開交易、不會拿鎖。沒有註冊 validator 的 request 直接放行。
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IRequestValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IRequestValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var errors = _validators.SelectMany(v => v.Validate(request)).ToList();

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        return await next();
    }
}
