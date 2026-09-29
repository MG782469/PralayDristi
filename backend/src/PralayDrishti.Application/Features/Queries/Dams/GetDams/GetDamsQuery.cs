using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Dams.GetDams;

public sealed record GetDamsQuery : IRequest<IReadOnlyList<DamDto>>, ICacheableQuery
{
    public string CacheKey => CacheKeys.Dams;
    public TimeSpan Expiry => TimeSpan.FromMinutes(10);
}
