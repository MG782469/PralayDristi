using MediatR;
using PralayDrishti.Application.Common.Models;

namespace PralayDrishti.Application.Features.Commands.Auth.RefreshToken;

public sealed record RefreshTokenCommand(string RefreshToken, string? Ip) : IRequest<AuthResult>;
