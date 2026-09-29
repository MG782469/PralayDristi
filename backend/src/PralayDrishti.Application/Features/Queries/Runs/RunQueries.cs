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

namespace PralayDrishti.Application.Features.Queries.Runs;

public sealed record GetRunsQuery(RunOrigin? Origin, int Take = 100) : IRequest<IReadOnlyList<RunSummaryDto>>;

public sealed class GetRunsQueryHandler : IRequestHandler<GetRunsQuery, IReadOnlyList<RunSummaryDto>>
{
    private readonly ISimulationRunRepository _runs;

    public GetRunsQueryHandler(ISimulationRunRepository runs) => _runs = runs;

    public async Task<IReadOnlyList<RunSummaryDto>> Handle(GetRunsQuery request, CancellationToken ct) =>
        (await _runs.ListAsync(request.Origin, Math.Clamp(request.Take, 1, 500), ct)).Select(r => r.ToDto()).ToList();
}

public sealed record GetRunStatusQuery(Guid Id) : IRequest<RunSummaryDto>;

public sealed class GetRunStatusQueryHandler : IRequestHandler<GetRunStatusQuery, RunSummaryDto>
{
    private readonly ISimulationRunRepository _runs;

    public GetRunStatusQueryHandler(ISimulationRunRepository runs) => _runs = runs;

    public async Task<RunSummaryDto> Handle(GetRunStatusQuery request, CancellationToken ct) =>
        (await _runs.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("Run", request.Id)).ToDto();
}

/// <summary>Full result for a completed run, or just the summary while it is queued / running.</summary>
public sealed record GetRunDetailQuery(Guid Id) : IRequest<JsonPayload>;

public sealed class GetRunDetailQueryHandler : IRequestHandler<GetRunDetailQuery, JsonPayload>
{
    private readonly ISimulationRunRepository _runs;
    private readonly IResultStore _store;
    private readonly ICacheService _cache;

    public GetRunDetailQueryHandler(ISimulationRunRepository runs, IResultStore store, ICacheService cache)
    {
        _runs = runs;
        _store = store;
        _cache = cache;
    }

    public async Task<JsonPayload> Handle(GetRunDetailQuery request, CancellationToken ct)
    {
        var run = await _runs.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("Run", request.Id);
        var summary = run.ToDto();
        if (run.Status != RunStatus.Completed)
            return new JsonPayload(JsonSerializer.Serialize(new { summary }, Mappings.Json));

        // completed results never change, so they are safe to cache (only cached once completed)
        var cached = await _cache.GetAsync<JsonPayload>(CacheKeys.RunDetail(run.Id), ct);
        if (cached != null) return cached;
        var detail = await _store.GetDetailAsync(run.Id, summary, ct)
                     ?? new JsonPayload(JsonSerializer.Serialize(new { summary, error = "result files missing" }, Mappings.Json));
        await _cache.SetAsync(CacheKeys.RunDetail(run.Id), detail, TimeSpan.FromHours(1), ct);
        return detail;
    }
}

public sealed record GetRiverProfileQuery(Guid Id) : IRequest<JsonPayload>, ICacheableQuery
{
    public string CacheKey => CacheKeys.RunProfile(Id);
    public TimeSpan Expiry => TimeSpan.FromHours(1);
}

public sealed class GetRiverProfileQueryHandler : IRequestHandler<GetRiverProfileQuery, JsonPayload>
{
    private readonly IResultStore _store;

    public GetRiverProfileQueryHandler(IResultStore store) => _store = store;

    public async Task<JsonPayload> Handle(GetRiverProfileQuery request, CancellationToken ct) =>
        await _store.GetProfileAsync(request.Id, ct) ?? throw new NotFoundException("Run results", request.Id);
}

public sealed record SamplePointQuery(Guid Id, double Lon, double Lat) : IRequest<JsonPayload>;

public sealed class SamplePointQueryValidator : AbstractValidator<SamplePointQuery>
{
    public SamplePointQueryValidator()
    {
        RuleFor(x => x.Lon).InclusiveBetween(-180, 180);
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
    }
}

public sealed class SamplePointQueryHandler : IRequestHandler<SamplePointQuery, JsonPayload>
{
    private readonly IResultStore _store;

    public SamplePointQueryHandler(IResultStore store) => _store = store;

    public async Task<JsonPayload> Handle(SamplePointQuery request, CancellationToken ct) =>
        await _store.SamplePointAsync(request.Id, request.Lon, request.Lat, ct) ?? throw new NotFoundException("Run results", request.Id);
}

public sealed record ExportRunQuery(Guid Id, string Format) : IRequest<FileResultDto>;

public sealed class ExportRunQueryValidator : AbstractValidator<ExportRunQuery>
{
    private static readonly string[] Known = { "shp", "kmz", "kml", "geojson", "cap", "pdf", "csv", "asc", "delft3d", "dualsphysics" };

    public ExportRunQueryValidator() =>
        RuleFor(x => x.Format).Must(f => Known.Contains(f?.ToLowerInvariant())).WithMessage($"Format must be one of: {string.Join(", ", Known)}");
}

public sealed class ExportRunQueryHandler : IRequestHandler<ExportRunQuery, FileResultDto>
{
    private readonly ISimulationRunRepository _runs;
    private readonly IExportService _exports;

    public ExportRunQueryHandler(ISimulationRunRepository runs, IExportService exports)
    {
        _runs = runs;
        _exports = exports;
    }

    public async Task<FileResultDto> Handle(ExportRunQuery request, CancellationToken ct)
    {
        var run = await _runs.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("Run", request.Id);
        if (run.Status != RunStatus.Completed) throw new BusinessRuleException("The run has not completed yet.");
        return await _exports.ExportAsync(run.Id, request.Format.ToLowerInvariant(), ct);
    }
}

public sealed record GetLayerImageQuery(Guid Id, string Layer) : IRequest<byte[]>;

public sealed class GetLayerImageQueryHandler : IRequestHandler<GetLayerImageQuery, byte[]>
{
    private readonly IMapRenderer _renderer;

    public GetLayerImageQueryHandler(IMapRenderer renderer) => _renderer = renderer;

    public Task<byte[]> Handle(GetLayerImageQuery request, CancellationToken ct) =>
        Task.FromResult(_renderer.RenderLayerPng(request.Id, request.Layer) ?? throw new NotFoundException($"Layer {request.Layer} of run", request.Id));
}
