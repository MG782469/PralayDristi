using MediatR;
using PralayDrishti.Application.Common.Models;

namespace PralayDrishti.Application.Features.Queries.Auth.GetCurrentUser;

public sealed record GetCurrentUserQuery : IRequest<UserDto>;
