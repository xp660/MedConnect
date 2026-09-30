using MediatR;
using MedConnect.Api.Contracts.Auth;
using MedConnect.Application.Auth.Commands.Login;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedConnect.Api.Controllers;

// 全域 FallbackPolicy 預設所有端點都需要驗證（見 Program.cs）；Login 本身就是取得 Token 的
// 入口，必須明確標記匿名可存取，否則會變成「要先有 Token 才能登入拿 Token」的死結。
[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
public sealed class AuthController : ControllerBase
{
    private readonly ISender _sender;

    public AuthController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var command = new LoginCommand(request.Email, request.Password);
        var result = await _sender.Send(command, cancellationToken);

        var response = new LoginResponse(result.AccessToken, "Bearer", result.ExpiresInSeconds);
        return Ok(response);
    }
}
