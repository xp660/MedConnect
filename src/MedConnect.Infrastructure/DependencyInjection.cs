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
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IPatientRepository, PatientRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.SecretKey), "Missing Jwt:SecretKey configuration (use `dotnet user-secrets set Jwt:SecretKey ...`).")
            .ValidateOnStart();

        return services;
    }
}
