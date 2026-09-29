using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Application.Features.Queries.Analysis.CalculateBreach;

public sealed class CalculateBreachQueryHandler : IRequestHandler<CalculateBreachQuery, JsonPayload>
{
    private readonly IHydraulicAnalysis _analysis;

    public CalculateBreachQueryHandler(IHydraulicAnalysis analysis) => _analysis = analysis;

    public Task<JsonPayload> Handle(CalculateBreachQuery r, CancellationToken ct) => Task.FromResult(
        _analysis.CalculateBreach(r.Type, r.HeightM, r.WaterDepthM, r.VolumeMcm, r.Mode, r.CrestLengthM, r.CrestWidthM, r.StorageExponent));
}
