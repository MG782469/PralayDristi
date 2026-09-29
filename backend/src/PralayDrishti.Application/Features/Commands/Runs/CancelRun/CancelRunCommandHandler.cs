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

namespace PralayDrishti.Application.Features.Commands.Runs.CancelRun;

public sealed class CancelRunCommandHandler : IRequestHandler<CancelRunCommand, RunSummaryDto>
{
    private readonly ISimulationRunRepository _runs;
    private readonly IUnitOfWork _uow;
    private readonly IBackgroundJobScheduler _scheduler;
    private readonly ICacheService _cache;
    private readonly IDateTimeProvider _clock;
    private readonly IRunNotifier _notifier;

    public CancelRunCommandHandler(ISimulationRunRepository runs, IUnitOfWork uow, IBackgroundJobScheduler scheduler,
        ICacheService cache, IDateTimeProvider clock, IRunNotifier notifier)
    {
        _runs = runs;
        _uow = uow;
        _scheduler = scheduler;
        _cache = cache;
        _clock = clock;
        _notifier = notifier;
    }

    public async Task<RunSummaryDto> Handle(CancelRunCommand request, CancellationToken ct)
    {
        var run = await _runs.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("Run", request.Id);
        switch (run.Status)
        {
            case RunStatus.Queued:
                if (run.JobId != null) _scheduler.Delete(run.JobId);
                run.Cancel(_clock.UtcNow);
                await _uow.SaveChangesAsync(ct);
                await _notifier.StatusAsync(run.Id, RunStatus.Cancelled.ToString());
                break;
            case RunStatus.Running:
                // the worker may be another process; it polls this flag and stops itself
                await _cache.SetAsync(CacheKeys.RunCancel(run.Id), true, TimeSpan.FromHours(2), ct);
                break;
            default:
                throw new BusinessRuleException($"Run is already {run.Status}.");
        }
        return run.ToDto();
    }
}
