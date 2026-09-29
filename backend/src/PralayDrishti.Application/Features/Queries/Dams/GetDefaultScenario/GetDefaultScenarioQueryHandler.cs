using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Dams.GetDefaultScenario;

public sealed class GetDefaultScenarioQueryHandler : IRequestHandler<GetDefaultScenarioQuery, ScenarioSpec>
{
    private readonly IDamRepository _dams;
    private readonly IFloodSimulationEngine _engine;

    public GetDefaultScenarioQueryHandler(IDamRepository dams, IFloodSimulationEngine engine)
    {
        _dams = dams;
        _engine = engine;
    }

    public async Task<ScenarioSpec> Handle(GetDefaultScenarioQuery request, CancellationToken ct)
    {
        var dam = await _dams.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("Dam", request.Id);
        return _engine.DefaultScenario(dam, await _dams.ListAsync(ct), request.Cascade);
    }
}
