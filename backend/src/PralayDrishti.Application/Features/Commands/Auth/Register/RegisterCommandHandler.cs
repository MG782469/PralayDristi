using MediatR;
using Microsoft.Extensions.Options;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Common.Options;
using PralayDrishti.Application.Services;
using PralayDrishti.Domain.Entities;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Commands.Auth.Register;

public sealed class RegisterCommandHandler : IRequestHandler<RegisterCommand, AuthResult>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly AuthService _auth;
    private readonly IDateTimeProvider _clock;
    private readonly AuthOptions _options;

    public RegisterCommandHandler(IUserRepository users, IPasswordHasher hasher, AuthService auth, IDateTimeProvider clock,
        IOptions<AuthOptions> options)
    {
        _users = users;
        _hasher = hasher;
        _auth = auth;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<AuthResult> Handle(RegisterCommand request, CancellationToken ct)
    {
        if (!_options.AllowSelfRegistration)
            throw new BusinessRuleException("Self-registration is disabled. Ask an administrator for an account.");
        if (await _users.ExistsAsync(User.Normalize(request.Email), ct))
            throw new ConflictException("An account with this email already exists.");

        // self-registered users can look, not act - an Admin promotes them
        var user = User.Create(request.Email, request.FullName, "", UserRole.Viewer, _clock.UtcNow);
        user.SetPasswordHash(_hasher.Hash(user, request.Password));
        user.MarkLogin(_clock.UtcNow);
        await _users.AddAsync(user, ct);
        return await _auth.IssueAsync(user, request.Ip, ct);
    }
}
