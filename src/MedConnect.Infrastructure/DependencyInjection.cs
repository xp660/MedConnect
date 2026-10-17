using MedConnect.Application.Abstractions;
using MedConnect.Infrastructure.Persistence;
using MedConnect.Infrastructure.Persistence.Interceptors;
using MedConnect.Infrastructure.Persistence.Repositories;
using MedConnect.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedConnect.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<ConcurrencyVersionInterceptor>();

        services.AddDbContext<MedConnectDbContext>((sp, options) =>
        {
            var connectionString = configuration.GetConnectionString("MedConnect")
                ?? throw new InvalidOperationException("Missing ConnectionStrings:MedConnect configuration.");

            options
                .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
                .AddInterceptors(sp.GetRequiredService<ConcurrencyVersionInterceptor>());
        });

        services.AddScoped<IScheduleSlotRepository, ScheduleSlotRepository>();
        services.AddScoped<IScheduleSlotQueryRepository, ScheduleSlotQueryRepository>();
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IDoctorRepository, DoctorRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.SecretKey), "Missing Jwt:SecretKey configuration (use `dotnet user-secrets set Jwt:SecretKey ...`).")
            // 缺密鑰由上一條報告；這一條只管「有設但太短」。錯誤訊息絕不包含密鑰的值或任何片段。
            .Validate(
                o => string.IsNullOrWhiteSpace(o.SecretKey) || System.Text.Encoding.UTF8.GetByteCount(o.SecretKey) >= JwtOptions.MinSecretKeyBytes,
                $"Jwt:SecretKey is too short: HS256 needs at least {JwtOptions.MinSecretKeyBytes} bytes (UTF-8).")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer), "Missing Jwt:Issuer configuration.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Audience), "Missing Jwt:Audience configuration.")
            .ValidateOnStart();

        return services;
    }
}
