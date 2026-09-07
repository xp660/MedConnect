using MedConnect.Infrastructure.Persistence;
using MedConnect.Infrastructure.Persistence.Interceptors;
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

        return services;
    }
}
