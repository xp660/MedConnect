using MediatR;
using MedConnect.Api.Contracts.Auth;
using MedConnect.Application.Auth.Commands.Login;
using Microsoft.AspNetCore.Mvc;

namespace MedConnect.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
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
