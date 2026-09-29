using MediatR;

namespace PralayDrishti.Application.Features.Commands.Auth.Logout;

public sealed record LogoutCommand(string? RefreshToken, string? Ip) : IRequest<Unit>;
