using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Application.Features.Queries.Analysis.InverseThreshold;

public sealed class InverseThresholdQueryHandler : IRequestHandler<InverseThresholdQuery, JsonPayload>
{
    private readonly IHydraulicAnalysis _analysis;

    public InverseThresholdQueryHandler(IHydraulicAnalysis analysis) => _analysis = analysis;

    public Task<JsonPayload> Handle(InverseThresholdQuery r, CancellationToken ct) =>
        _analysis.InverseThresholdAsync(r.RunId, r.Lon, r.Lat, r.Depth, r.TargetName, ct);
}
