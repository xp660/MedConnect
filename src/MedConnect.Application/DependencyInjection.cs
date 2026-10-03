using System.Reflection;
using MediatR;
using MedConnect.Application.Common.Behaviors;
using MedConnect.Application.Common.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
            // Validation 在外層、Retry 在內層：不合法的輸入不該進入重試迴圈（architecture-plan.md §0 v1.9）。
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(RetryBehavior<,>));
        });

        // TryAdd：讓測試能在 AddApplication() 之前先註冊自己的 RetryOptions（例如把延遲設為 0）。
        services.TryAddSingleton(new RetryOptions());

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
