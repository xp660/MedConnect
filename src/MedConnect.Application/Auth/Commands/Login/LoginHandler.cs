using MediatR;
using MedConnect.Application.Abstractions;
using MedConnect.Application.Common.Exceptions;

namespace MedConnect.Application.Auth.Commands.Login;

public sealed class LoginHandler : IRequestHandler<LoginCommand, GeneratedToken>
{
    // 帳號不存在時比對用的假雜湊值，只在第一次用到時透過 IPasswordHasher 算出來並快取。
    // 用 static + `??=` 而非 lock：就算多個請求同時撞到還沒算好的第一次、各自算出一份
    // （雜湊內容不同但格式都合法），也只是重複做一次無害的運算，不影響正確性，
    // 換取不必為這種次要用途引入鎖。
    private static string? _dummyPasswordHash;

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

    public async Task<GeneratedToken> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);

        // 帳號不存在時，仍然對一組假雜湊值執行一次真正的 BCrypt Verify，而不是直接短路跳過——
        // 否則「帳號不存在」會比「帳號存在但密碼錯」快非常多，讓攻擊者能用回應時間差
        // 推測 email 是否已註冊（timing side-channel），變相繞過下面刻意收斂成同一種例外
        // 想防的 User Enumeration。
        var passwordHash = user?.PasswordHash ?? (_dummyPasswordHash ??= _passwordHasher.Hash("timing-side-channel-mitigation-dummy-password"));
        var passwordIsValid = _passwordHasher.Verify(request.Password, passwordHash);

        // 帳號不存在、密碼錯誤刻意收斂成同一條路徑、同一種例外（見 InvalidCredentialsException 的
        // XML 文件註解），防止 User Enumeration。這裡不能提前 return 或用不同例外分岔。
        if (user is null || !passwordIsValid)
        {
            throw new InvalidCredentialsException();
        }

        // 每個 Patient 角色的 User 在建立當下就一定會一併建立 Patient（見 DatabaseSeeder），
        // 這裡查不到屬於資料不一致的 bug，不是「帳號密碼錯誤」這種預期內事件，
        // 所以刻意不吞成 InvalidCredentialsException，讓它以未對映例外外洩成 500。
        var patient = await _patientRepository.GetByUserIdAsync(user.Id, cancellationToken)
            ?? throw new InvalidOperationException($"User {user.Id} has no associated Patient record.");

        return _tokenService.GenerateToken(user, patient.Id);
    }
}
