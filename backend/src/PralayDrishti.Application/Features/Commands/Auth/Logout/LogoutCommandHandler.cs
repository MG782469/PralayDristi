using MediatR;
using PralayDrishti.Application.Services;

namespace PralayDrishti.Application.Features.Commands.Auth.Logout;

public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand, Unit>
{
    private readonly AuthService _auth;

    public LogoutCommandHandler(AuthService auth) => _auth = auth;

    public async Task<Unit> Handle(LogoutCommand request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
            await _auth.RevokeAsync(request.RefreshToken, request.Ip, ct);
        return Unit.Value;
    }
}
