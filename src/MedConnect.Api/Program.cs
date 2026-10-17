using System.Text;
using MedConnect.Api.ExceptionHandling;
using MedConnect.Api.Hosting;
using MedConnect.Api.Logging;
using MedConnect.Application;
using MedConnect.Infrastructure;
using MedConnect.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;

// 啟動失敗（缺 JWT 密鑰的 OptionsValidationException、migration 失敗…）也要留下日誌：
// host 建好之前沒有 DI 的 logger，所以用 ApiLogging.CreateStartupLogger() 自己持有的 logger 記 Fatal，
// 再原樣重新丟出（程序照樣以失敗結束），結束前 Dispose 會 flush。
using var startupLogger = ApiLogging.CreateStartupLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // preserveStaticLogger: true —— 每個 host 擁有自己的 logger，完全不碰靜態的 Log.Logger。
    // 預設（false）會把 logger 指派給全域的 Log.Logger，而且 host 的 ILoggerFactory 寫入時用的就是
    // 「當下的 Log.Logger」：同一個程序建立多個 host（整合測試的 WebApplicationFactory）時，後建立的 host
    // 會把先建立的 host 的日誌搶過去，host 釋放時還會把全域 logger 關掉。
    builder.Host.UseSerilog(ApiLogging.Configure, preserveStaticLogger: true);

    // Add services to the container.

    builder.Services.AddControllers()
        .ConfigureApiBehaviorOptions(options =>
        {
            // model binding 失敗（doctorId=abc、JSON 格式錯誤…）預設會回一個沒有 errorCode 的 400，
            // 跟 ValidationBehavior 產生的 400 形狀不同；統一走 ApiProblemDetails。
            options.InvalidModelStateResponseFactory = ApiProblemDetails.FromInvalidModelState;
        });
    // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
    builder.Services.AddOpenApi();

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer();

    // JwtBearerOptions 刻意透過 Configure<IOptions<JwtOptions>> 注入 AddInfrastructure() 裡
    // 已經綁定好的同一份 JwtOptions（同一個 "Jwt" 設定區段、同一把 user-secrets 密鑰、
    // 同一套 fail-fast 驗證），不在這裡重新讀一次 configuration——避免 Login 簽發跟這裡
    // 驗證的密鑰/Issuer/Audience 出自兩個獨立維護的設定路徑，未來改一邊忘記改另一邊。
    builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
        .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptions) =>
        {
            var jwt = jwtOptions.Value;
            bearerOptions.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = jwt.Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey)),
            };
        });

    builder.Services.AddAuthorization(options =>
    {
        // Fail-safe default（架構決策，見這次 1d JWT middleware 的討論）：沒有明確標記
        // [Authorize]/[AllowAnonymous] 的端點一律視為需要驗證。之後新增端點若忘記標注，
        // 預設行為是「擋下來」而不是「悄悄開放」。
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    });

    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();

    builder.Services.AddHealthChecks();

    var app = builder.Build();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        // OpenAPI schema 本身不含任何機密、僅供本機瀏覽用途，FallbackPolicy 生效後若不明確
        // 標記，會連 Swagger/OpenAPI 文件都被擋在驗證後面，對本機開發沒有安全效益、只有麻煩。
        app.MapOpenApi().AllowAnonymous();
    }

    // migrate 與 seed 由兩個明確的設定開關決定，不看環境名稱（architecture-plan.md §0 v1.13）；預設都是 false。
    // Development 的 appsettings 把兩個都設成 true，所以本機開發與整合測試的行為與以前完全相同。
    var startupSettings = StartupSettings.From(app.Configuration);

    if (startupSettings.MigrateOnStartup)
    {
        app.Logger.LogInformation("{Setting} is enabled: applying database migrations", StartupSettings.MigrateOnStartupKey);
        await app.Services.MigrateAsync();
    }

    if (startupSettings.SeedDemoData)
    {
        // 措辭刻意是「可能」：seeder 遇到資料庫已有資料會直接跳過，這行只表示開關是開的。
        // 不可包含示範帳號的密碼或 email。
        app.Logger.LogWarning(
            "{Setting} is enabled: demo data including an account with a publicly known password may be created. Never enable this against a real database",
            StartupSettings.SeedDemoDataKey);
        await app.Services.SeedAsync();
    }

    app.UseExceptionHandler();

    // 容器內只聽 HTTP 8080、TLS 由外層負責時沒有 https 埠可轉，middleware 每個請求都會警告；compose 把它關掉。
    if (startupSettings.UseHttpsRedirection)
    {
        app.UseHttpsRedirection();
    }

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHealthChecks("/health").AllowAnonymous();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    // HostAbortedException 是 dotnet-ef / WebApplicationFactory 這類工具刻意讓 host 提前中止用的，不是故障。
    startupLogger.Fatal(ex, "Host terminated unexpectedly");
    throw;
}

public partial class Program
{
    // WebApplicationFactory<Program> 需要這個型別在組件外部可見，才能讓
    // MedConnect.IntegrationTests 透過真正的 ASP.NET Core middleware pipeline
    // （含這次加入的 JWT Bearer 驗證）打 HTTP 請求，而不是繞過 Api 層直接呼叫 Handler。
}
