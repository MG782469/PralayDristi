using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Application.Features.Queries.Analysis.EstimateReservoir;

public sealed class EstimateReservoirQueryHandler : IRequestHandler<EstimateReservoirQuery, JsonPayload>
{
    private readonly IHydraulicAnalysis _analysis;

    public EstimateReservoirQueryHandler(IHydraulicAnalysis analysis) => _analysis = analysis;

    public Task<JsonPayload> Handle(EstimateReservoirQuery r, CancellationToken ct) =>
        _analysis.EstimateReservoirAsync(r.Lon, r.Lat, r.AreaKm2, r.AltimetryLevel, r.DeadStorageMcm, r.MaxRise, ct);
}
