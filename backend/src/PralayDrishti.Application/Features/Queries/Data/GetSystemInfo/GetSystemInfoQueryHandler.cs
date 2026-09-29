using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Data.GetSystemInfo;

public sealed class GetSystemInfoQueryHandler : IRequestHandler<GetSystemInfoQuery, SystemInfoDto>
{
    private readonly IEnvironmentInfo _env;
    private readonly IDamRepository _dams;
    private readonly ISimulationRunRepository _runs;
    private readonly IAlertRepository _alerts;
    private readonly IFileStorage _files;
    private readonly IExportService _exports;

    public GetSystemInfoQueryHandler(IEnvironmentInfo env, IDamRepository dams, ISimulationRunRepository runs, IAlertRepository alerts,
        IFileStorage files, IExportService exports)
    {
        _env = env;
        _dams = dams;
        _runs = runs;
        _alerts = alerts;
        _files = files;
        _exports = exports;
    }

    public async Task<SystemInfoDto> Handle(GetSystemInfoQuery request, CancellationToken ct)
    {
        int running = (await _runs.ListByStatusAsync(new[] { RunStatus.Running }, ct)).Count;
        return new SystemInfoDto("PralayDrishti", typeof(GetSystemInfoQuery).Assembly.GetName().Version?.ToString(), _env.Offline,
            _env.OsmEnabled, _env.Delft3DConfigured, _env.RedisConnected, running, (await _dams.ListAsync(ct)).Count,
            await _runs.CountAsync(ct), await _alerts.CountAsync(ct), Math.Round(_files.TerrainCacheBytes() / 1024.0 / 1024.0, 1),
            _exports.Formats, Environment.MachineName, Environment.ProcessorCount);
    }
}
