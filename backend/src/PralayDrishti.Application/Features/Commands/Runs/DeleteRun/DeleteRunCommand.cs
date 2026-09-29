using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Common.Validation;
using PralayDrishti.Application.Services;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Commands.Runs.DeleteRun;

public sealed record DeleteRunCommand(Guid Id) : IRequest<Unit>, ICacheInvalidator
{
    public IEnumerable<string> InvalidatesKeys => new[] { CacheKeys.RunDetail(Id), CacheKeys.RunProfile(Id), CacheKeys.Leaderboard };
}
