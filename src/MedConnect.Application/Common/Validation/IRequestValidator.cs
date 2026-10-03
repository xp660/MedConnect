namespace MedConnect.Application.Common.Validation;

/// <summary>
/// 手寫的 request 驗證（不引入 FluentValidation，見 architecture-plan.md §0 v1.8）。
///
/// 只檢查「結構」：必填、長度、數值範圍。**絕不查資料庫**——「這筆資料存不存在、是不是你的」是
/// Handler 的責任，那裡才有防 Enumeration 的邏輯（例如 Login 帳號不存在與密碼錯誤收斂成同一個例外、
/// Cancel 的「不存在」與「不是你的」收斂成同一個例外）。若驗證與存在性判斷拆在兩處，
/// 防 Enumeration 的規則就會散落、容易被其中一處破壞。
/// </summary>
public interface IRequestValidator<in TRequest>
{
    IReadOnlyList<ValidationError> Validate(TRequest request);
}

/// <summary>Field 用「對外（wire）的名稱」，也就是 JSON / query / route 的參數名，不是 C# 屬性名。</summary>
public sealed record ValidationError(string Field, string Message);
