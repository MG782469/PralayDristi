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

namespace PralayDrishti.Application.Features.Queries.Runs.GetRunDetail;

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
