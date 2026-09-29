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

namespace PralayDrishti.Application.Features.Queries.Runs.GetRunStatus;

public sealed class GetRunStatusQueryHandler : IRequestHandler<GetRunStatusQuery, RunSummaryDto>
{
    private readonly ISimulationRunRepository _runs;

    public GetRunStatusQueryHandler(ISimulationRunRepository runs) => _runs = runs;

    public async Task<RunSummaryDto> Handle(GetRunStatusQuery request, CancellationToken ct) =>
        (await _runs.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("Run", request.Id)).ToDto();
}
