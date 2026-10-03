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
/// 防止 User Enumeration。這裡的失敗測試刻意斷言同一個例外型別、同一個訊息。
/// </summary>
public class LoginHandlerTests
{
    private const long PatientId = 42;
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();

    private LoginHandler CreateHandler() => new(_userRepository, _passwordHasher, _tokenService);

    private static User CreateUser(string email = "patient@medconnect.local", string passwordHash = "hashed-value") =>
        new(email, passwordHash, UserRole.Patient, Now);

    private void GivenCandidate(User user, long? patientId) =>
        _userRepository.GetWithPatientIdByEmailAsync(user.Email, Arg.Any<CancellationToken>())
            .Returns(new UserWithPatientId(user, patientId));

    [Fact]
    public async Task Handle_WhenUserDoesNotExist_ThrowsInvalidCredentials()
    {
        _userRepository.GetWithPatientIdByEmailAsync("nobody@medconnect.local", Arg.Any<CancellationToken>())
            .Returns((UserWithPatientId?)null);
        var command = new LoginCommand("nobody@medconnect.local", "whatever");

        var act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCredentialsException>()
            .WithMessage(InvalidCredentialsMessage);
        // 刻意斷言「有」呼叫 Verify：帳號不存在時仍要對一組假雜湊值跑一次真正的 BCrypt
        // Verify，讓耗時跟「帳號存在但密碼錯」的路徑一致，避免用回應時間差推測帳號是否存在。
        _passwordHasher.Received(1).Verify(command.Password, Arg.Any<string>());
        _tokenService.DidNotReceive().GenerateToken(Arg.Any<User>(), Arg.Any<long>());
    }

    [Fact]
    public async Task Handle_WhenPasswordIsWrong_ThrowsSameInvalidCredentialsExceptionAsUserNotFound()
    {
        var user = CreateUser();
        GivenCandidate(user, PatientId);
        _passwordHasher.Verify("wrong-password", user.PasswordHash).Returns(false);
        var command = new LoginCommand(user.Email, "wrong-password");

        var act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        // 刻意斷言跟「帳號不存在」完全相同的型別與訊息：外部無法區分是哪一種失敗。
        await act.Should().ThrowAsync<InvalidCredentialsException>()
            .WithMessage(InvalidCredentialsMessage);
        _tokenService.DidNotReceive().GenerateToken(Arg.Any<User>(), Arg.Any<long>());
    }

    [Fact]
    public async Task Handle_WhenCredentialsAreValid_IssuesTokenForTheAssociatedPatient()
    {
        var user = CreateUser();
        GivenCandidate(user, PatientId);
        _passwordHasher.Verify("correct-password", user.PasswordHash).Returns(true);
        var issued = new GeneratedToken("signed-jwt", 3600);
        _tokenService.GenerateToken(user, PatientId).Returns(issued);
        var command = new LoginCommand(user.Email, "correct-password");

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.Should().Be(issued);
        _tokenService.Received(1).GenerateToken(user, PatientId);
    }

    [Fact]
    public async Task Handle_WhenPasswordIsCorrectButPatientRecordIsMissing_ThrowsInvalidOperationRatherThanInvalidCredentials()
    {
        // 這不是使用者可控的輸入錯誤，是資料不一致的 bug，刻意不吞成 InvalidCredentialsException
        // （見 LoginHandler 的註解），讓它以未對映例外外洩成 500，而不是被誤判成帳密錯誤。
        var user = CreateUser();
        GivenCandidate(user, patientId: null);
        _passwordHasher.Verify("correct-password", user.PasswordHash).Returns(true);
        var command = new LoginCommand(user.Email, "correct-password");

        var act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowExactlyAsync<InvalidOperationException>();
        _tokenService.DidNotReceive().GenerateToken(Arg.Any<User>(), Arg.Any<long>());
    }

    [Fact]
    public async Task Handle_WhenPasswordIsWrongAndPatientRecordIsMissing_StillThrowsInvalidCredentialsNeverTheDataIntegrityError()
    {
        // 硬性要求：必須先驗證密碼、才檢查 PatientId。
        // User 與 PatientId 是同一次查詢取回的，所以「缺 Patient 記錄」在驗證密碼之前就已經知道了；
        // 但只要有人把 PatientId 的檢查挪到密碼驗證之前，不知道密碼的人就能從 500（InvalidOperation）
        // vs 401（InvalidCredentials）分辨出「這個 email 有帳號、但缺 Patient 記錄」——
        // 一條新的帳號枚舉管道。這個測試就是為了在那種重構發生時變紅。
        var user = CreateUser();
        GivenCandidate(user, patientId: null);
        _passwordHasher.Verify("wrong-password", user.PasswordHash).Returns(false);
        var command = new LoginCommand(user.Email, "wrong-password");

        var act = async () => await CreateHandler().Handle(command, CancellationToken.None);

        // ThrowExactly：不接受 InvalidCredentialsException 的父類別或任何其他例外型別。
        var thrown = await act.Should().ThrowExactlyAsync<InvalidCredentialsException>();
        // 型別與訊息都必須跟「帳號不存在」「密碼錯誤」完全一致：外部無法區分這是第三種情況。
        thrown.Which.Message.Should().Be(InvalidCredentialsMessage);
        // 密碼驗證確實有跑（證明是「驗證失敗」擋下的，不是別的原因），且沒有簽發任何 token。
        _passwordHasher.Received(1).Verify("wrong-password", user.PasswordHash);
        _tokenService.DidNotReceive().GenerateToken(Arg.Any<User>(), Arg.Any<long>());
    }
}
