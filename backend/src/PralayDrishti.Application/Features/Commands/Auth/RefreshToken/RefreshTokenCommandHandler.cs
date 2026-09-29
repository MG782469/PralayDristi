using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Services;

namespace PralayDrishti.Application.Features.Commands.Auth.RefreshToken;

public sealed class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthResult>
{
    private readonly AuthService _auth;

    public RefreshTokenCommandHandler(AuthService auth) => _auth = auth;

    public Task<AuthResult> Handle(RefreshTokenCommand request, CancellationToken ct) => _auth.RefreshAsync(request.RefreshToken, request.Ip, ct);
}
