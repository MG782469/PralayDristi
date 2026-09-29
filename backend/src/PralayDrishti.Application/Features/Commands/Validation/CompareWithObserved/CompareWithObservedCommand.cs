using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Models;

namespace PralayDrishti.Application.Features.Commands.Validation.CompareWithObserved;

/// <summary>Real-event check: simulated extent vs Sentinel-1 flood polygons (from gee/s1_flood_map.py). Result is stored for the scoreboard.</summary>
public sealed record CompareWithObservedCommand(Guid RunId, string ObservedGeoJson, string? PermanentWaterGeoJson, string? ArrivalsJson,
    string? EventName, double DepthThreshold = 0.3) : IRequest<JsonPayload>, ICacheInvalidator
{
    public IEnumerable<string> InvalidatesKeys => new[] { CacheKeys.AnalyticChecks };
}
