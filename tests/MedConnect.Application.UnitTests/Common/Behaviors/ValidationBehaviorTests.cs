using FluentAssertions;
using MediatR;
using MedConnect.Application.Common.Behaviors;
using MedConnect.Application.Common.Exceptions;
using MedConnect.Application.Common.Validation;
using NSubstitute;
using Xunit;

namespace MedConnect.Application.UnitTests.Common.Behaviors;

public class ValidationBehaviorTests
{
    public sealed record SampleRequest(string Name) : IRequest<string>;

    private sealed class FixedValidator(params ValidationError[] errors) : IRequestValidator<SampleRequest>
    {
        public IReadOnlyList<ValidationError> Validate(SampleRequest request) => errors;
    }

    private static ValidationBehavior<SampleRequest, string> CreateBehavior(params IRequestValidator<SampleRequest>[] validators) =>
        new(validators);

    [Fact]
    public async Task Handle_WhenThereAreNoValidators_CallsNextAndReturnsItsResult()
    {
        var next = Substitute.For<RequestHandlerDelegate<string>>();
        next.Invoke().Returns("handler result");

        var result = await CreateBehavior().Handle(new SampleRequest("x"), next, CancellationToken.None);

        result.Should().Be("handler result");
        await next.Received(1).Invoke();
    }

    [Fact]
    public async Task Handle_WhenAllValidatorsPass_CallsNextExactlyOnce()
    {
        var next = Substitute.For<RequestHandlerDelegate<string>>();
        next.Invoke().Returns("ok");

        var result = await CreateBehavior(new FixedValidator(), new FixedValidator())
            .Handle(new SampleRequest("x"), next, CancellationToken.None);

        result.Should().Be("ok");
        await next.Received(1).Invoke();
    }

    [Fact]
    public async Task Handle_WhenAValidatorFails_ThrowsAndNeverCallsTheHandler()
    {
        var next = Substitute.For<RequestHandlerDelegate<string>>();
        var behavior = CreateBehavior(new FixedValidator(new ValidationError("name", "is required")));

        var act = async () => await behavior.Handle(new SampleRequest(""), next, CancellationToken.None);

        var thrown = (await act.Should().ThrowExactlyAsync<RequestValidationException>()).Which;
        thrown.Errors.Should().ContainSingle().Which.Should().Be(new ValidationError("name", "is required"));
        // 驗證失敗時 Handler 完全不能被執行：它一旦執行就可能開交易、拿鎖、寫資料。
        await next.DidNotReceive().Invoke();
    }

    [Fact]
    public async Task Handle_WhenSeveralValidatorsFail_AggregatesAllTheirErrors()
    {
        var next = Substitute.For<RequestHandlerDelegate<string>>();
        var behavior = CreateBehavior(
            new FixedValidator(new ValidationError("a", "bad a")),
            new FixedValidator(new ValidationError("b", "bad b1"), new ValidationError("b", "bad b2")));

        var act = async () => await behavior.Handle(new SampleRequest(""), next, CancellationToken.None);

        var thrown = (await act.Should().ThrowExactlyAsync<RequestValidationException>()).Which;
        thrown.Errors.Should().BeEquivalentTo(new[]
        {
            new ValidationError("a", "bad a"),
            new ValidationError("b", "bad b1"),
            new ValidationError("b", "bad b2"),
        });
    }
}
