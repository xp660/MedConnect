using System.Reflection;
using MediatR;
using MedConnect.Application.Common.Behaviors;
using MedConnect.Application.Common.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace MedConnect.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);

            // 註冊順序 = 由外而內的執行順序（先註冊的包在最外層）。
            // architecture-plan.md §5.1 規劃的順序是 Logging → Validation → Performance；
            // 目前只有 Validation，Logging / Performance 留到 1e（Serilog / OpenTelemetry），
            // 屆時依序加在它的前後即可。
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        AddRequestValidators(services, assembly);

        return services;
    }

    /// <summary>
    /// MediatR 的 RegisterServicesFromAssembly 只認得它自己的 handler 型別，不認得 IRequestValidator&lt;&gt;，
    /// 所以這裡自己掃描：用幾行 reflection 就夠，不為了這件事引入 Scrutor。
    /// </summary>
    private static void AddRequestValidators(IServiceCollection services, Assembly assembly)
    {
        var validatorInterface = typeof(IRequestValidator<>);

        var registrations = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == validatorInterface)
                .Select(i => (Service: i, Implementation: t)));

        foreach (var (service, implementation) in registrations)
        {
            services.AddTransient(service, implementation);
        }
    }
}
