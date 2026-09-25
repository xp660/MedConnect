namespace MedConnect.Infrastructure.Security;

/// <summary>
/// 綁定自 appsettings 的 "Jwt" 區段。SecretKey 刻意不放在這裡的預設值或 appsettings.Development.json——
/// 那個檔案沒有被 .gitignore 排除，會直接外洩進 git。SecretKey 一律用 `dotnet user-secrets` 存放，
/// 缺少時直接在啟動時丟例外（見 DependencyInjection.cs），不可用假密鑰頂著跑。
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string SecretKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 60;
}
