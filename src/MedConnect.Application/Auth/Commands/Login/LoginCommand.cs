using MediatR;
using MedConnect.Application.Abstractions;

namespace MedConnect.Application.Auth.Commands.Login;

public sealed record LoginCommand(string Email, string Password) : IRequest<GeneratedToken>
{
    // 與 Api 的 LoginRequest 同理：預設的 record ToString() 會印出明文密碼，任何人把整個 command
    // 丟進 logger（字串插值、或框架在 Trace 層級印參數）都會把密碼寫進日誌。
    public override string ToString() => "LoginCommand { Email = ***, Password = *** }";
}
