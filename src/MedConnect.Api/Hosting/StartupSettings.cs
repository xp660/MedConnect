namespace MedConnect.Api.Hosting;

/// <summary>
/// 啟動行為的明確設定開關（architecture-plan.md §0 v1.13）。預設全部是「安全」的值：
/// 什麼都不設的環境不會 migrate、不會建立任何示範帳號。刻意不用「是不是 Development 環境」當開關——
/// 環境名稱是太粗的訊號：Docker 展示環境是 Production 卻需要示範資料，而一個設定鍵只代表一件事、
/// 開了什麼可以從設定與啟動日誌直接看出來。
/// </summary>
public sealed record StartupSettings(bool MigrateOnStartup, bool SeedDemoData, bool UseHttpsRedirection)
{
    /// <summary>啟動時自動套用 EF migration。只適用單一實例；水平擴展時應改成獨立的 migration 工作。</summary>
    public const string MigrateOnStartupKey = "Database:MigrateOnStartup";

    /// <summary>啟動時建立「密碼公開的示範帳號」與示範診次（資料庫已有資料時 seeder 會直接跳過）。</summary>
    public const string SeedDemoDataKey = "Database:SeedDemoData";

    /// <summary>是否啟用 HTTPS 轉址。預設 true；容器內只聽 HTTP 8080、由外層負責 TLS 時設成 false。</summary>
    public const string UseHttpsRedirectionKey = "Hosting:UseHttpsRedirection";

    public static StartupSettings From(IConfiguration configuration) => new(
        MigrateOnStartup: configuration.GetValue(MigrateOnStartupKey, false),
        SeedDemoData: configuration.GetValue(SeedDemoDataKey, false),
        UseHttpsRedirection: configuration.GetValue(UseHttpsRedirectionKey, true));
}
