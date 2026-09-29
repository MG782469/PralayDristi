using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Common.Validation;
using PralayDrishti.Application.Services;
using PralayDrishti.Domain.Entities;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Commands.Dams.SimulateDam;

public sealed class SimulateDamCommandHandler : IRequestHandler<SimulateDamCommand, RunSummaryDto>
{
    private readonly IDamRepository _dams;
    private readonly IFloodSimulationEngine _engine;
    private readonly RunFactory _factory;

    public SimulateDamCommandHandler(IDamRepository dams, IFloodSimulationEngine engine, RunFactory factory)
    {
        _dams = dams;
        _engine = engine;
        _factory = factory;
    }

    public async Task<RunSummaryDto> Handle(SimulateDamCommand request, CancellationToken ct)
    {
        var dam = await _dams.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("Dam", request.Id);
        var spec = _engine.DefaultScenario(dam, await _dams.ListAsync(ct), request.Cascade);
        return (await _factory.QueueAsync(spec, RunOrigin.User, dam.Id, null, ct)).ToDto();
    }
}
