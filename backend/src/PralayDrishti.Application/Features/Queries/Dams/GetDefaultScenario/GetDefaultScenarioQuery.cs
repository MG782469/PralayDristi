using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Dams.GetDefaultScenario;

public sealed record GetDefaultScenarioQuery(int Id, bool Cascade) : IRequest<ScenarioSpec>, ICacheableQuery
{
    public string CacheKey => CacheKeys.DamScenario(Id, Cascade);
    public TimeSpan Expiry => TimeSpan.FromMinutes(10);
}
