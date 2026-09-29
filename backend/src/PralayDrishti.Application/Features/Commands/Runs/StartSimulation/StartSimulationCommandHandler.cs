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

namespace PralayDrishti.Application.Features.Commands.Runs.StartSimulation;

public sealed class StartSimulationCommandHandler : IRequestHandler<StartSimulationCommand, RunSummaryDto>
{
    private readonly RunFactory _factory;

    public StartSimulationCommandHandler(RunFactory factory) => _factory = factory;

    public async Task<RunSummaryDto> Handle(StartSimulationCommand request, CancellationToken ct) =>
        (await _factory.QueueAsync(request.Spec, RunOrigin.User, request.DamId, null, ct)).ToDto();
}
