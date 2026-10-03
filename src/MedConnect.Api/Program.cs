using System.Text;
using MedConnect.Api.ExceptionHandling;
using MedConnect.Application;
using MedConnect.Infrastructure;
using MedConnect.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

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
    await app.Services.MigrateAndSeedAsync();
}

app.UseExceptionHandler();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

public partial class Program
{
    // WebApplicationFactory<Program> 需要這個型別在組件外部可見，才能讓
    // MedConnect.IntegrationTests 透過真正的 ASP.NET Core middleware pipeline
    // （含這次加入的 JWT Bearer 驗證）打 HTTP 請求，而不是繞過 Api 層直接呼叫 Handler。
}
