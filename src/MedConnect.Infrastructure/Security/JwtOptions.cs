namespace MedConnect.Infrastructure.Security;

/// <summary>
/// 綁定自 appsettings 的 "Jwt" 區段。SecretKey 刻意不放在這裡的預設值或 appsettings.Development.json——
/// 那個檔案沒有被 .gitignore 排除，會直接外洩進 git。SecretKey 一律用 `dotnet user-secrets` 存放，
/// 缺少時直接在啟動時丟例外（見 DependencyInjection.cs），不可用假密鑰頂著跑。
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// HS256 要求金鑰至少 256 bits。用 UTF-8「位元組數」而不是字元數：金鑰是被 UTF-8 編碼後才拿去簽章的，
    /// 11 個中文字是 33 位元組（夠），32 個字元不一定夠。實測（System.IdentityModel.Tokens.Jwt 8.23.0）：
    /// 31 位元組簽章時丟 IDX10720，32 位元組通過——但那要等到第一次 Login 才會爆，所以改在啟動時就檢查。
    /// </summary>
    public const int MinSecretKeyBytes = 32;

    public string SecretKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 60;
}
