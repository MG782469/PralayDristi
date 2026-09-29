using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Features.Commands.Auth.Register;
using PralayDrishti.Application.Services;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Commands.Auth.ChangePassword;

public sealed class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Unit>
{
    private readonly ICurrentUserService _me;
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly AuthService _auth;

    public ChangePasswordCommandHandler(ICurrentUserService me, IUserRepository users, IPasswordHasher hasher, AuthService auth)
    {
        _me = me;
        _users = users;
        _hasher = hasher;
        _auth = auth;
    }

    public async Task<Unit> Handle(ChangePasswordCommand request, CancellationToken ct)
    {
        var id = _me.UserId ?? throw new ForbiddenAccessException();
        var user = await _users.GetByIdAsync(id, ct) ?? throw new NotFoundException("User", id);
        if (!_hasher.Verify(user, user.PasswordHash, request.CurrentPassword))
            throw new ForbiddenAccessException("Current password is wrong.");
        user.SetPasswordHash(_hasher.Hash(user, request.NewPassword));
        // every other session has to sign in again
        await _auth.RevokeAllAsync(user.Id, "password-changed", ct);
        return Unit.Value;
    }
}
