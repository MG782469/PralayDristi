using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Models;

namespace PralayDrishti.Application.Features.Commands.Validation.RunBenchmark;

/// <summary>Generic "DEM + reservoir polygon + observed gauges" benchmark (Malpasset and friends) from data/benchmarks.</summary>
public sealed record RunBenchmarkCommand(string Name) : IRequest<JsonPayload>, ICacheInvalidator, ILongRunningRequest
{
    public IEnumerable<string> InvalidatesKeys => new[] { CacheKeys.AnalyticChecks };
}
