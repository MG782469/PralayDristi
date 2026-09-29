using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Services;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Commands.Users.ChangeUserRole;

public sealed class ChangeUserRoleCommandHandler : IRequestHandler<ChangeUserRoleCommand, UserDto>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _me;

    public ChangeUserRoleCommandHandler(IUserRepository users, IUnitOfWork uow, ICurrentUserService me)
    {
        _users = users;
        _uow = uow;
        _me = me;
    }

    public async Task<UserDto> Handle(ChangeUserRoleCommand request, CancellationToken ct)
    {
        if (request.UserId == _me.UserId && request.Role != UserRole.Admin)
            throw new BusinessRuleException("You cannot remove your own Admin role.");
        var user = await _users.GetByIdAsync(request.UserId, ct) ?? throw new NotFoundException("User", request.UserId);
        user.ChangeRole(request.Role);
        await _uow.SaveChangesAsync(ct);
        return user.ToDto();
    }
}
