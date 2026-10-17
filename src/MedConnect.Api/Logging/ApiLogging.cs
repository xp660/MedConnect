using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;

namespace MedConnect.Api.Logging;

/// <summary>
/// Serilog 只存在於 Api（host）層（architecture-plan.md §0 v1.12）；Domain / Application 只用 ILogger&lt;T&gt;，
/// 由 MedConnect.ArchitectureTests 強制。
/// </summary>
public static class ApiLogging
{
    // 以下「事件」是框架在例外被 GlobalExceptionHandler 記錄之前就已經各自記下的 Error，
    // 對同一個例外重複記錄；而且已對映的 409/404/400 是正常事件，不該有任何 Error 層級的日誌。
    // 過濾條件是「類別 + 事件 Id」，不是整個類別：同類別的其他事件（例如錯誤處理器自己丟出例外）保留。
    // Api 不可使用 EF 型別（§4.2），所以 EF 的事件 Id 寫成常數；數值全部來自實測
    // （重複預約：20102 + 10000；真實死結：20102 + 20205 + 10000——20205 是死結路徑才會多出來的第三筆，
    //  事先推測時漏掉了，是 DeadlockLoggingTests 在真實 MySQL 上抓出來的）。
    //   ASP.NET：ExceptionHandlerMiddleware 1 UnhandledException；Hosting.Internal.Host 11 HostedServiceStartupFaulted。
    //   20102 RelationalEventId.CommandError、20205 RelationalEventId.TransactionError、
    //   10000 CoreEventId.SaveChangesFailed。
    private static readonly (string SourceContext, int EventId)[] RedundantErrorEvents =
    [
        ("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", 1),
        // 啟動失敗：host 自己記一筆「Hosting failed to start」(Error)，Program.cs 的 catch 又記一筆 Fatal，
        // 同一個例外兩行。Fatal 那筆一定會有（app.Run() 丟出的例外一定走到 catch），所以過濾框架這筆。
        ("Microsoft.Extensions.Hosting.Internal.Host", 11),
        ("Microsoft.EntityFrameworkCore.Database.Command", 20102),
        ("Microsoft.EntityFrameworkCore.Database.Transaction", 20205),
        ("Microsoft.EntityFrameworkCore.Update", 10000),
    ];

    /// <summary>
    /// Console 輸出的最低層級（預設不限制，跟 logger 的最低層級一致）。獨立於 Serilog:MinimumLevel：
    /// logger 最低層級決定「哪些事件存在」（包含接在 DI 的捕捉 sink），這個只決定「Console 印不印」。
    /// 測試需要 logger 開到 Verbose 去檢查「任何一行日誌」，又不想讓測試輸出被數百行 Debug 淹沒時用它。
    /// </summary>
    public const string ConsoleMinimumLevelKey = "Serilog:Console:MinimumLevel";

    private const string DevelopmentConsoleTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}";

    /// <summary>
    /// 正式的 logger 設定。過濾規則設在 logger 的根層級，所以所有 sink——包含測試從 DI 掛進來的捕捉 sink——
    /// 都在過濾之後才收到事件：測試看到的就是正式環境會輸出的內容。
    /// </summary>
    public static void Configure(HostBuilderContext context, IServiceProvider services, LoggerConfiguration configuration)
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            // 讓 DI 裡註冊的 ILogEventSink（只有測試會註冊）也接進這條 logger。
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Filter.ByExcluding(IsRedundantErrorEvent)
            .WriteTo.Console(
                CreateConsoleFormatter(context.HostingEnvironment.EnvironmentName),
                restrictedToMinimumLevel: context.Configuration.GetValue(ConsoleMinimumLevelKey, LogEventLevel.Verbose));
    }

    /// <summary>
    /// 啟動期間用的 logger：host 還沒建好（或建失敗）時，Fatal 要有地方可以寫。
    /// 刻意不用 Serilog 的 CreateBootstrapLogger()：它回傳的 ReloadableLogger 在 UseSerilog 時會被「凍結」，
    /// 同一個程序（例如整合測試）建立第二個 host 時會丟 "The logger is already frozen"。
    /// 這裡回傳的是普通的 Logger，由呼叫端自己持有、自己 Dispose（Dispose 會 flush），
    /// 完全不碰靜態的 Log.Logger，所以建幾個 host 都互不影響。
    /// </summary>
    public static Logger CreateStartupLogger()
    {
        // host 還沒建好，拿不到 IHostEnvironment；用跟 ASP.NET Core 相同的環境變數判斷格式。
        var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environments.Production;

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(CreateConsoleFormatter(environmentName))
            .CreateLogger();
    }

    public static bool IsRedundantErrorEvent(LogEvent logEvent)
    {
        if (logEvent.Level != LogEventLevel.Error
            || !logEvent.Properties.TryGetValue("SourceContext", out var sourceContext)
            || sourceContext is not ScalarValue { Value: string sourceContextName }
            || TryGetEventId(logEvent) is not { } eventId)
        {
            return false;
        }

        foreach (var redundant in RedundantErrorEvents)
        {
            if (redundant.EventId == eventId && redundant.SourceContext == sourceContextName)
            {
                return true;
            }
        }

        return false;
    }

    // Serilog 把 Microsoft.Extensions.Logging 的 EventId 轉成名為 "EventId"、帶 Id（與 Name）屬性的結構值。
    private static int? TryGetEventId(LogEvent logEvent)
    {
        if (logEvent.Properties.TryGetValue("EventId", out var value) && value is StructureValue structure)
        {
            foreach (var property in structure.Properties)
            {
                if (property.Name == "Id" && property.Value is ScalarValue { Value: int id })
                {
                    return id;
                }
            }
        }

        return null;
    }

    // Development：人在終端機看，可讀性優先。其他環境：一行一個 JSON（含 @t、@m、@l、@x 與所有具名參數），
    // 給日誌系統收；具名參數會變成可查詢的獨立欄位，不是只存在一段字串裡。
    // 公開、吃環境名稱字串：測試可以用「正式環境（非 Development）」的格式器去格式化真實捕捉到的事件。
    public static ITextFormatter CreateConsoleFormatter(string environmentName) =>
        string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase)
            ? new Serilog.Formatting.Display.MessageTemplateTextFormatter(DevelopmentConsoleTemplate)
            : new RenderedCompactJsonFormatter();
}
