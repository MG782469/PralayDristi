using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Services;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Commands.Users.SetUserActive;

public sealed class SetUserActiveCommandHandler : IRequestHandler<SetUserActiveCommand, UserDto>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _me;
    private readonly AuthService _auth;

    public SetUserActiveCommandHandler(IUserRepository users, IUnitOfWork uow, ICurrentUserService me, AuthService auth)
    {
        _users = users;
        _uow = uow;
        _me = me;
        _auth = auth;
    }

    public async Task<UserDto> Handle(SetUserActiveCommand request, CancellationToken ct)
    {
        if (request.UserId == _me.UserId && !request.Active)
            throw new BusinessRuleException("You cannot deactivate yourself.");
        var user = await _users.GetByIdAsync(request.UserId, ct) ?? throw new NotFoundException("User", request.UserId);
        user.SetActive(request.Active);
        await _uow.SaveChangesAsync(ct);
        if (!request.Active) await _auth.RevokeAllAsync(user.Id, "deactivated", ct);
        return user.ToDto();
    }
}
