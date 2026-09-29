using MediatR;
using PralayDrishti.Application.Common.Models;

namespace PralayDrishti.Application.Features.Commands.Auth.Register;

public sealed record RegisterCommand(string Email, string Password, string FullName, string? Ip) : IRequest<AuthResult>;
