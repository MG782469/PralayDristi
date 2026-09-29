using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Application.Features.Queries.Analysis.EstimateNaturalDam;

public sealed class EstimateNaturalDamQueryHandler : IRequestHandler<EstimateNaturalDamQuery, JsonPayload>
{
    private readonly IHydraulicAnalysis _analysis;

    public EstimateNaturalDamQueryHandler(IHydraulicAnalysis analysis) => _analysis = analysis;

    public Task<JsonPayload> Handle(EstimateNaturalDamQuery r, CancellationToken ct) =>
        _analysis.EstimateNaturalDamAsync(r.Polygon, r.CrestElevation, r.HeightAboveBed, r.CurrentLevel, r.Type, ct);
}
