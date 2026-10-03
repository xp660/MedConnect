using FluentAssertions;
using MediatR;
using MedConnect.Application.Appointments.Commands.BookAppointment;
using MedConnect.Application.Appointments.Commands.CancelAppointment;
using MedConnect.Application.Auth.Commands.Login;
using MedConnect.Application.Common.Behaviors;
using MedConnect.Application.Common.Validation;
using MedConnect.Application.ScheduleSlots.Queries.GetAvailableSlots;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedConnect.Application.UnitTests;

/// <summary>
/// 驗證 AddApplication() 真的把 ValidationBehavior 與各 validator 接進去了——
/// 少了這個，單獨測 validator 全綠，執行期卻完全沒有任何驗證在跑。
/// 直接檢查註冊描述（ServiceDescriptor），不需要真的建 ServiceProvider。
/// </summary>
public class DependencyInjectionTests
{
    private static ServiceCollection Register()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        return services;
    }

    [Fact]
    public void AddApplication_RegistersValidationBehaviorAsAnOpenGenericPipelineBehavior()
    {
        var services = Register();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>) && d.ImplementationType == typeof(ValidationBehavior<,>));
    }

    [Fact]
    public void AddApplication_RegistersRetryBehaviorInsideValidationBehavior()
    {
        var services = Register();

        var behaviors = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .ToList();

        // 註冊順序 = 由外而內的執行順序：Validation 必須在 Retry 之前，
        // 不合法的輸入才不會進入重試迴圈（同一個驗證錯誤被重試 3 次毫無意義）。
        behaviors.Should().Equal(typeof(ValidationBehavior<,>), typeof(RetryBehavior<,>));
    }

    [Theory]
    [InlineData(typeof(LoginCommand), typeof(LoginCommandValidator))]
    [InlineData(typeof(BookAppointmentCommand), typeof(BookAppointmentCommandValidator))]
    [InlineData(typeof(CancelAppointmentCommand), typeof(CancelAppointmentCommandValidator))]
    [InlineData(typeof(GetAvailableSlotsQuery), typeof(GetAvailableSlotsQueryValidator))]
    public void AddApplication_RegistersTheValidatorForEachUserFacingRequest(Type request, Type validator)
    {
        var services = Register();

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IRequestValidator<>).MakeGenericType(request) && d.ImplementationType == validator);
    }
}
