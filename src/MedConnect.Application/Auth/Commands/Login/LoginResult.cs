namespace MedConnect.Application.Auth.Commands.Login;

public sealed record LoginResult(string AccessToken, int ExpiresInSeconds);
