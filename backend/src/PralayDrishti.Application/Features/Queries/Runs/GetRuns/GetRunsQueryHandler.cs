using System.Text.Json;
using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Runs.GetRuns;

public sealed class GetRunsQueryHandler : IRequestHandler<GetRunsQuery, IReadOnlyList<RunSummaryDto>>
{
    private readonly ISimulationRunRepository _runs;

    public GetRunsQueryHandler(ISimulationRunRepository runs) => _runs = runs;

    public async Task<IReadOnlyList<RunSummaryDto>> Handle(GetRunsQuery request, CancellationToken ct) =>
        (await _runs.ListAsync(request.Origin, Math.Clamp(request.Take, 1, 500), ct)).Select(r => r.ToDto()).ToList();
}
