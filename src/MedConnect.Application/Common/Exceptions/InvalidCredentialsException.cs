namespace MedConnect.Application.Common.Exceptions;

/// <summary>
/// 帳號不存在與密碼錯誤刻意共用同一個例外型別、同一個訊息，避免 User Enumeration
/// （architecture-plan.md §7.1）。LoginHandler 的兩個失敗分支都必須丟這個型別，
/// 不可另外定義第二種「帳號不存在」例外，否則等於在型別層級洩漏了「哪一步失敗」。
/// </summary>
public sealed class InvalidCredentialsException : Exception
{
    public InvalidCredentialsException()
        : base("Invalid email or password.")
    {
    }
}
