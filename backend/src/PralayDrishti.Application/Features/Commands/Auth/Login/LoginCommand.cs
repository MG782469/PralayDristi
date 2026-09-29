using MediatR;
using PralayDrishti.Application.Common.Models;

namespace PralayDrishti.Application.Features.Commands.Auth.Login;

public sealed record LoginCommand(string Email, string Password, string? Ip) : IRequest<AuthResult>;
