using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Services;
using PralayDrishti.Domain.Entities;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Commands.Auth.Login;

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResult>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly AuthService _auth;
    private readonly IDateTimeProvider _clock;

    public LoginCommandHandler(IUserRepository users, IPasswordHasher hasher, AuthService auth, IDateTimeProvider clock)
    {
        _users = users;
        _hasher = hasher;
        _auth = auth;
        _clock = clock;
    }

    public async Task<AuthResult> Handle(LoginCommand request, CancellationToken ct)
    {
        var user = await _users.GetByEmailAsync(User.Normalize(request.Email), ct);
        // same message for unknown email and wrong password - don't leak which accounts exist
        if (user is null || !_hasher.Verify(user, user.PasswordHash, request.Password))
            throw new ForbiddenAccessException("Invalid email or password.");
        if (!user.IsActive) throw new ForbiddenAccessException("Account is disabled.");

        user.MarkLogin(_clock.UtcNow);
        return await _auth.IssueAsync(user, request.Ip, ct);
    }
}
