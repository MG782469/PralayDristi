using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Models;

namespace PralayDrishti.Application.Features.Commands.Validation.CompareWithObserved;

public sealed class CompareWithObservedCommandHandler : IRequestHandler<CompareWithObservedCommand, JsonPayload>
{
    private readonly IValidationBenchmarks _bench;

    public CompareWithObservedCommandHandler(IValidationBenchmarks bench) => _bench = bench;

    public Task<JsonPayload> Handle(CompareWithObservedCommand r, CancellationToken ct) =>
        _bench.CompareAsync(r.RunId, r.ObservedGeoJson, r.PermanentWaterGeoJson, r.ArrivalsJson, r.EventName, r.DepthThreshold, ct);
}
