using FluentAssertions;
using MedConnect.Application.Auth.Commands.Login;
using MedConnect.Application.Common.Validation;
using Xunit;

namespace MedConnect.Application.UnitTests.Auth.Commands.Login;

public class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator _validator = new();

    [Fact]
    public void Validate_WithValidEmailAndPassword_ReturnsNoErrors()
    {
        _validator.Validate(new LoginCommand("patient@medconnect.local", "Test1234!")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_WithMissingEmail_ReturnsEmailRequired(string? email)
    {
        _validator.Validate(new LoginCommand(email!, "x")).Should()
            .ContainSingle().Which.Should().Be(new ValidationError("email", "is required"));
    }

    [Fact]
    public void Validate_WithEmailOfExactlyTheColumnLimit_IsAccepted()
    {
        var email = new string('a', 256);

        _validator.Validate(new LoginCommand(email, "x")).Should().BeEmpty();
    }

    [Fact]
    public void Validate_WithEmailOverTheColumnLimit_ReturnsTooLong()
    {
        var email = new string('a', 257);

        _validator.Validate(new LoginCommand(email, "x")).Should()
            .ContainSingle().Which.Should().Be(new ValidationError("email", "must be at most 256 characters"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_WithMissingPassword_ReturnsPasswordRequired(string? password)
    {
        _validator.Validate(new LoginCommand("a@b.com", password!)).Should()
            .ContainSingle().Which.Should().Be(new ValidationError("password", "is required"));
    }

    [Fact]
    public void Validate_WithWhitespaceOnlyPassword_IsAccepted()
    {
        // 密碼可以合法地包含空白，不能被 Trim 後當成空的。
        _validator.Validate(new LoginCommand("a@b.com", "   ")).Should().BeEmpty();
    }

    [Fact]
    public void Validate_DoesNotCheckEmailFormat_SoItCannotBecomeAnAccountExistenceOracle()
    {
        // 只檢查結構：格式是否像 email、帳號是否存在，都不是這一層的責任。
        _validator.Validate(new LoginCommand("not-an-email", "x")).Should().BeEmpty();
    }

    [Fact]
    public void Validate_WithBothMissing_ReturnsBothErrors()
    {
        _validator.Validate(new LoginCommand("", "")).Select(e => e.Field).Should().BeEquivalentTo("email", "password");
    }
}
