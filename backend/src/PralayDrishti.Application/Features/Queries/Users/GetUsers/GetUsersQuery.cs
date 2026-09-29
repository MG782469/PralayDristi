using MediatR;
using PralayDrishti.Application.Common.Models;

namespace PralayDrishti.Application.Features.Queries.Users.GetUsers;

public sealed record GetUsersQuery : IRequest<IReadOnlyList<UserDto>>;
