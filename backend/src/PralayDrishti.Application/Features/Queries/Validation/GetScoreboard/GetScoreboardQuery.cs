using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Models;

namespace PralayDrishti.Application.Features.Queries.Validation.GetScoreboard;

public sealed record GetScoreboardQuery : IRequest<JsonPayload>, ICacheableQuery, ILongRunningRequest
{
    public string CacheKey => CacheKeys.AnalyticChecks;
    public TimeSpan Expiry => TimeSpan.FromMinutes(30);
}
