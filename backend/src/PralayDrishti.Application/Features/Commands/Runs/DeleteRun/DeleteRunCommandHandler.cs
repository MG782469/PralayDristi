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

namespace PralayDrishti.Application.Features.Commands.Runs.DeleteRun;

public sealed class DeleteRunCommandHandler : IRequestHandler<DeleteRunCommand, Unit>
{
    private readonly ISimulationRunRepository _runs;
    private readonly IUnitOfWork _uow;
    private readonly IBackgroundJobScheduler _scheduler;
    private readonly ICacheService _cache;
    private readonly IResultStore _store;

    public DeleteRunCommandHandler(ISimulationRunRepository runs, IUnitOfWork uow, IBackgroundJobScheduler scheduler, ICacheService cache,
        IResultStore store)
    {
        _runs = runs;
        _uow = uow;
        _scheduler = scheduler;
        _cache = cache;
        _store = store;
    }

    public async Task<Unit> Handle(DeleteRunCommand request, CancellationToken ct)
    {
        var run = await _runs.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("Run", request.Id);
        if (run.Status == RunStatus.Queued && run.JobId != null) _scheduler.Delete(run.JobId);
        if (run.Status == RunStatus.Running) await _cache.SetAsync(CacheKeys.RunCancel(run.Id), true, TimeSpan.FromHours(2), ct);
        _runs.Remove(run);
        await _uow.SaveChangesAsync(ct);
        _store.Delete(run.Id);
        return Unit.Value;
    }
}
