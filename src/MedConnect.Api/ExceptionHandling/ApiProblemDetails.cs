using System.Text.Json;
using MedConnect.Application.Common.Validation;
using Microsoft.AspNetCore.Mvc;

namespace MedConnect.Api.ExceptionHandling;

/// <summary>
/// 所有錯誤回應的唯一建構點（architecture-plan.md §7：一律 application/problem+json，含 errorCode、traceId）。
/// 400 有兩個來源——MediatR 的 ValidationBehavior（業務層驗證）與 [ApiController] 的 model binding 失敗
/// （例如 doctorId=abc、JSON 格式錯誤）——兩者都必須走這裡，才不會出現兩種不同形狀的 400。
/// </summary>
public static class ApiProblemDetails
{
    public const string ContentType = "application/problem+json";
    public const string ValidationFailedErrorCode = "VALIDATION_FAILED";
    private const string ValidationFailedDetail = "One or more validation errors occurred.";

    public static ProblemDetails Create(HttpContext httpContext, int statusCode, string errorCode, string detail)
    {
        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = errorCode,
            Detail = detail,
        };
        problemDetails.Extensions["errorCode"] = errorCode;
        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;

        return problemDetails;
    }

    // statusCode 由呼叫端傳入：回應的 HTTP 狀態碼與 body 裡的 status 必須是同一個值，
    // 不在這裡再寫死一次 400（呼叫端：GlobalExceptionHandler 的對映表、FromInvalidModelState）。
    public static ProblemDetails CreateValidationFailed(HttpContext httpContext, int statusCode, IReadOnlyDictionary<string, string[]> errors)
    {
        var problemDetails = Create(httpContext, statusCode, ValidationFailedErrorCode, ValidationFailedDetail);
        problemDetails.Extensions["errors"] = errors;

        return problemDetails;
    }

    /// <summary>欄位 → 訊息陣列，跟 ASP.NET 內建 ValidationProblemDetails 的 errors 同形狀。</summary>
    public static IReadOnlyDictionary<string, string[]> ToErrors(IEnumerable<ValidationError> errors) =>
        errors
            .GroupBy(e => e.Field)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray());

    /// <summary>
    /// 給 ApiBehaviorOptions.InvalidModelStateResponseFactory 用：model binding 失敗時，
    /// 回跟 ValidationBehavior 一樣形狀的 400（多了 errorCode，且 traceId 用跟其他錯誤相同的來源）。
    /// ASP.NET 預設的 key 是 PascalCase（例如 "Email"），這裡轉成 camelCase 對齊 JSON 欄位名；
    /// 以 "$." 開頭的 JSON 路徑與 "request" 這類非屬性 key 不受影響。
    /// </summary>
    public static IActionResult FromInvalidModelState(ActionContext context)
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors
                    .Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "The input was not valid." : e.ErrorMessage)
                    .ToArray());

        const int statusCode = StatusCodes.Status400BadRequest;

        return new ObjectResult(CreateValidationFailed(context.HttpContext, statusCode, errors))
        {
            StatusCode = statusCode,
            ContentTypes = { ContentType },
        };
    }
}
