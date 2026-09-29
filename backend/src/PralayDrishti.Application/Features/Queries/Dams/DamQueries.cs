using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Dams;

public sealed record GetDamsQuery : IRequest<IReadOnlyList<DamDto>>, ICacheableQuery
{
    public string CacheKey => CacheKeys.Dams;
    public TimeSpan Expiry => TimeSpan.FromMinutes(10);
}

public sealed class GetDamsQueryHandler : IRequestHandler<GetDamsQuery, IReadOnlyList<DamDto>>
{
    private readonly IDamRepository _dams;

    public GetDamsQueryHandler(IDamRepository dams) => _dams = dams;

    public async Task<IReadOnlyList<DamDto>> Handle(GetDamsQuery request, CancellationToken ct) =>
        (await _dams.ListAsync(ct)).Select(d => d.ToDto()).ToList();
}

public sealed record GetDamByIdQuery(int Id) : IRequest<DamDto>;

public sealed class GetDamByIdQueryHandler : IRequestHandler<GetDamByIdQuery, DamDto>
{
    private readonly IDamRepository _dams;

    public GetDamByIdQueryHandler(IDamRepository dams) => _dams = dams;

    public async Task<DamDto> Handle(GetDamByIdQuery request, CancellationToken ct) =>
        (await _dams.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("Dam", request.Id)).ToDto();
}

public sealed record GetDefaultScenarioQuery(int Id, bool Cascade) : IRequest<ScenarioSpec>, ICacheableQuery
{
    public string CacheKey => CacheKeys.DamScenario(Id, Cascade);
    public TimeSpan Expiry => TimeSpan.FromMinutes(10);
}

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
