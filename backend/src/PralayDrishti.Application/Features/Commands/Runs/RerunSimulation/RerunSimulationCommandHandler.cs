using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Common.Validation;
using PralayDrishti.Application.Services;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Commands.Runs.RerunSimulation;

public sealed class RerunSimulationCommandHandler : IRequestHandler<RerunSimulationCommand, RunSummaryDto>
{
    private readonly ISimulationRunRepository _runs;
    private readonly RunFactory _factory;

    public RerunSimulationCommandHandler(ISimulationRunRepository runs, RunFactory factory)
    {
        _runs = runs;
        _factory = factory;
    }

    public async Task<RunSummaryDto> Handle(RerunSimulationCommand request, CancellationToken ct)
    {
        var run = await _runs.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("Run", request.Id);
        var copy = await _factory.QueueAsync(RunFactory.ReadSpec(run), run.Origin, run.DamId, null, ct);
        return copy.ToDto();
    }
}
