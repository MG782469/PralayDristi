using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Auth.GetCurrentUser;

public sealed class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, UserDto>
{
    private readonly ICurrentUserService _me;
    private readonly IUserRepository _users;

    public GetCurrentUserQueryHandler(ICurrentUserService me, IUserRepository users)
    {
        _me = me;
        _users = users;
    }

    public async Task<UserDto> Handle(GetCurrentUserQuery request, CancellationToken ct)
    {
        var id = _me.UserId ?? throw new ForbiddenAccessException();
        var user = await _users.GetByIdAsync(id, ct) ?? throw new NotFoundException("User", id);
        return user.ToDto();
    }
}
