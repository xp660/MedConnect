using FluentAssertions;
using MedConnect.Application.Abstractions;
using MedConnect.Application.Auth.Commands.Login;
using MedConnect.Application.Common.Exceptions;
using MedConnect.Domain.Entities;
using MedConnect.Domain.Enums;
using NSubstitute;
using Xunit;

namespace MedConnect.Application.UnitTests.Auth.Commands.Login;

/// <summary>
/// architecture-plan.md §7.1：帳號不存在與密碼錯誤必須在型別和內容上都無法被外部區分，
/// 防止 User Enumeration。這裡的兩個失敗測試刻意斷言同一個例外型別、同一個訊息。
/// </summary>
public class LoginHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPatientRepository _patientRepository = Substitute.For<IPatientRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();

    private LoginHandler CreateHandler() =>
        new(_userRepository, _patientRepository, _passwordHasher, _tokenService);

    private static User CreateUser(string email = "patient@medconnect.local", string passwordHash = "hashed-value") =>
        new(email, passwordHash, UserRole.Patient, Now);

    [Fact]
    public async Task Handle_WhenUserDoesNotExist_ThrowsInvalidCredentials()
    {
        _userRepository.GetByEmailAsync("nobody@medconnect.local", Arg.Any<CancellationToken>())
            .Returns((User?)null);
        var command = new LoginCommand("nobody@medconnect.local", "whatever");

        var act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCredentialsException>()
            .WithMessage("Invalid email or password.");
        // 刻意斷言「有」呼叫 Verify：帳號不存在時仍要對一組假雜湊值跑一次真正的 BCrypt
        // Verify，讓耗時跟「帳號存在但密碼錯」的路徑一致，避免用回應時間差推測帳號是否存在。
        _passwordHasher.Received(1).Verify(command.Password, Arg.Any<string>());
        _tokenService.DidNotReceive().GenerateToken(Arg.Any<User>(), Arg.Any<long>());
    }

    [Fact]
    public async Task Handle_WhenPasswordIsWrong_ThrowsSameInvalidCredentialsExceptionAsUserNotFound()
    {
        var user = CreateUser();
        _userRepository.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify("wrong-password", user.PasswordHash).Returns(false);
        var command = new LoginCommand(user.Email, "wrong-password");

        var act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        // 刻意斷言跟「帳號不存在」完全相同的型別與訊息：外部無法區分是哪一種失敗。
        await act.Should().ThrowAsync<InvalidCredentialsException>()
            .WithMessage("Invalid email or password.");
        _tokenService.DidNotReceive().GenerateToken(Arg.Any<User>(), Arg.Any<long>());
    }

    [Fact]
    public async Task Handle_WhenCredentialsAreValid_IssuesTokenForAssociatedPatient()
    {
        var user = CreateUser();
        var patient = new Patient(user.Id, "Test Patient", Now);
        _userRepository.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify("correct-password", user.PasswordHash).Returns(true);
        _patientRepository.GetByUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(patient);
        _tokenService.GenerateToken(user, patient.Id).Returns(new GeneratedToken("signed-jwt", 3600));
        var command = new LoginCommand(user.Email, "correct-password");

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.AccessToken.Should().Be("signed-jwt");
        result.ExpiresInSeconds.Should().Be(3600);
    }

    [Fact]
    public async Task Handle_WhenUserHasNoAssociatedPatient_ThrowsInvalidOperationRatherThanInvalidCredentials()
    {
        // 這不是使用者可控的輸入錯誤，是資料不一致的 bug，刻意不吞成 InvalidCredentialsException
        // （見 LoginHandler 的註解），讓它以未對映例外外洩成 500，而不是被誤判成帳密錯誤。
        var user = CreateUser();
        _userRepository.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify("correct-password", user.PasswordHash).Returns(true);
        _patientRepository.GetByUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns((Patient?)null);
        var command = new LoginCommand(user.Email, "correct-password");

        var act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
