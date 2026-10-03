using MediatR;
using MedConnect.Application.Abstractions;

namespace MedConnect.Application.Auth.Commands.Login;

public sealed record LoginCommand(string Email, string Password) : IRequest<GeneratedToken>;
