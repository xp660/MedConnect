using FluentAssertions;
using MedConnect.Application.Auth.Commands.Login;
using Xunit;

namespace MedConnect.Application.UnitTests.Auth.Commands.Login;

public class LoginCommandToStringTests
{
    [Fact]
    public void ToString_NeverExposesThePasswordOrTheEmail()
    {
        var command = new LoginCommand("someone@example.test", "Super-Secret-Password-1!");

        var text = command.ToString();

        text.Should().NotContain("Super-Secret-Password-1!");
        text.Should().NotContain("someone@example.test");
    }
}
