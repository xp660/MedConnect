using MediatR;
using MedConnect.Application.Abstractions;
using MedConnect.Application.Common.Exceptions;

namespace MedConnect.Application.Auth.Commands.Login;

public sealed class LoginHandler : IRequestHandler<LoginCommand, LoginResult>
{
    private readonly IUserRepository _userRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;

    public LoginHandler(
        IUserRepository userRepository,
        IPatientRepository patientRepository,
        IPasswordHasher passwordHasher,
        ITokenService tokenService)
    {
        _userRepository = userRepository;
        _patientRepository = patientRepository;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    public async Task<LoginResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);

        // 帳號不存在、密碼錯誤刻意收斂成同一條路徑、同一種例外（見 InvalidCredentialsException 的
        // XML 文件註解），防止 User Enumeration。這裡不能提前 return 或用不同例外分岔。
        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new InvalidCredentialsException();
        }

        // 每個 Patient 角色的 User 在建立當下就一定會一併建立 Patient（見 DatabaseSeeder），
        // 這裡查不到屬於資料不一致的 bug，不是「帳號密碼錯誤」這種預期內事件，
        // 所以刻意不吞成 InvalidCredentialsException，讓它以未對映例外外洩成 500。
        var patient = await _patientRepository.GetByUserIdAsync(user.Id, cancellationToken)
            ?? throw new InvalidOperationException($"User {user.Id} has no associated Patient record.");

        var token = _tokenService.GenerateToken(user, patient.Id);

        return new LoginResult(token.AccessToken, token.ExpiresInSeconds);
    }
}
