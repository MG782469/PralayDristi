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

namespace PralayDrishti.Application.Features.Commands.Runs;

public sealed record StartSimulationCommand(ScenarioSpec Spec, int? DamId) : IRequest<RunSummaryDto>;

public sealed class StartSimulationCommandValidator : AbstractValidator<StartSimulationCommand>
{
    public StartSimulationCommandValidator() => RuleFor(x => x.Spec).NotNull().SetValidator(new ScenarioSpecValidator());
}

public sealed class StartSimulationCommandHandler : IRequestHandler<StartSimulationCommand, RunSummaryDto>
{
    private readonly RunFactory _factory;

    public StartSimulationCommandHandler(RunFactory factory) => _factory = factory;

    public async Task<RunSummaryDto> Handle(StartSimulationCommand request, CancellationToken ct) =>
        (await _factory.QueueAsync(request.Spec, RunOrigin.User, request.DamId, null, ct)).ToDto();
}

public sealed record CancelRunCommand(Guid Id) : IRequest<RunSummaryDto>;

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

public sealed record RerunSimulationCommand(Guid Id) : IRequest<RunSummaryDto>;

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

public sealed record DeleteRunCommand(Guid Id) : IRequest<Unit>, ICacheInvalidator
{
    public IEnumerable<string> InvalidatesKeys => new[] { CacheKeys.RunDetail(Id), CacheKeys.RunProfile(Id), CacheKeys.Leaderboard };
}

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

public sealed record RaiseAlertCommand(Guid RunId, AlertStatus Status) : IRequest<AlertDto>;

public sealed class RaiseAlertCommandValidator : AbstractValidator<RaiseAlertCommand>
{
    public RaiseAlertCommandValidator()
    {
        RuleFor(x => x.RunId).NotEmpty();
        RuleFor(x => x.Status).IsInEnum();
    }
}

public sealed class RaiseAlertCommandHandler : IRequestHandler<RaiseAlertCommand, AlertDto>
{
    private readonly ISimulationRunRepository _runs;
    private readonly AlertService _alerts;

    public RaiseAlertCommandHandler(ISimulationRunRepository runs, AlertService alerts)
    {
        _runs = runs;
        _alerts = alerts;
    }

    public async Task<AlertDto> Handle(RaiseAlertCommand request, CancellationToken ct)
    {
        var run = await _runs.GetByIdAsync(request.RunId, ct) ?? throw new NotFoundException("Run", request.RunId);
        if (run.Status != RunStatus.Completed) throw new BusinessRuleException("Alerts can only be raised for completed runs.");
        return await _alerts.CreateForRunAsync(run.Id, request.Status, ct);
    }
}
