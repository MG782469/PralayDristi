using FluentValidation.TestHelper;
using Microsoft.Extensions.Logging.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Features.Commands.Dams.CreateDam;
using PralayDrishti.Application.Features.Commands.Dams.DeleteDam;
using PralayDrishti.Application.Features.Commands.Lakes.IngestDetections;
using PralayDrishti.Application.Features.Commands.Runs.CancelRun;
using PralayDrishti.Application.Features.Commands.Runs.StartSimulation;
using PralayDrishti.Application.Services;
using PralayDrishti.Domain.Entities;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.UnitTests.Fakes;

namespace PralayDrishti.UnitTests.Application;

public class FeatureTests
{
    private readonly FakeRunRepo _runs = new();
    private readonly FakeDamRepo _dams = new();
    private readonly FakeLakeRepo _lakes = new();
    private readonly FakeScheduler _scheduler = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FakeUow _uow = new();
    private readonly FakeClock _clock = new();
    private readonly FakeCache _cache = new();
    private readonly FakeCurrentUser _me = new() { UserId = Guid.NewGuid(), Role = UserRole.Analyst };

    private RunFactory Factory() => new(_runs, _uow, _scheduler, _clock, _me);

    private static ScenarioSpec DamSpec() => new()
    {
        Name = "t",
        Dam = new DamSpec { Name = "T", Lon = 80, Lat = 25, HeightM = 30, ReservoirVolumeM3 = 1e7 }
    };

    [Fact]
    public void StartSimulation_validator_rejects_natural_mode_without_polygon()
    {
        var spec = DamSpec();
        spec.Mode = ScenarioMode.NaturalDam;
        var v = new StartSimulationCommandValidator();
        v.TestValidate(new StartSimulationCommand(spec, null)).ShouldHaveAnyValidationError();
        v.TestValidate(new StartSimulationCommand(DamSpec(), null)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task StartSimulation_creates_queued_run_and_enqueues_job()
    {
        var r = await new StartSimulationCommandHandler(Factory()).Handle(new StartSimulationCommand(DamSpec(), null), default);

        Assert.Equal(RunStatus.Queued, r.Status);
        var run = Assert.Single(_runs.Items);
        Assert.Equal(_me.UserId, run.CreatedByUserId);
        var job = Assert.Single(_scheduler.Enqueued);
        Assert.Equal(RunFactory.SimulationsQueue, job.Queue);
        Assert.Equal(job.Id, run.JobId);
    }

    [Fact]
    public async Task DeleteDam_missing_throws_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => new DeleteDamCommandHandler(_dams, _uow).Handle(new DeleteDamCommand(42), default));
    }

    [Fact]
    public void CreateDam_invalidates_dams_cache()
    {
        var cmd = new CreateDamCommand(new DamDto(0, "X", "R", "S", 25, 80, DamType.Earthfill, 30, 10, 300, 10, 40,
            DamCategory.Reservoir, true, "", "", new(), null));
        Assert.Contains(CacheKeys.Dams, cmd.InvalidatesKeys);
    }

    [Fact]
    public async Task CancelRun_on_queued_deletes_job_and_marks_cancelled()
    {
        var summary = await new StartSimulationCommandHandler(Factory()).Handle(new StartSimulationCommand(DamSpec(), null), default);
        await new CancelRunCommandHandler(_runs, _uow, _scheduler, _cache, _clock, _notifier).Handle(new CancelRunCommand(summary.Id), default);

        Assert.Equal(RunStatus.Cancelled, _runs.Items[0].Status);
        Assert.Contains(_runs.Items[0].JobId!, _scheduler.Deleted);
    }

    [Fact]
    public async Task IngestDetections_creates_lake_and_queues_run_when_auto()
    {
        var service = new LakeToAlertService(_lakes, _uow, new FakeEngine(), Factory(), _notifier, _clock,
            NullLogger<LakeToAlertService>.Instance);
        var ids = await new IngestDetectionsCommandHandler(service).Handle(
            new IngestDetectionsCommand(new[] { new LakeDetectionInput(30.5, 79.7, 0.3) }, true), default);

        var lake = Assert.Single(_lakes.Items);
        Assert.Equal(ids[0], lake.Id);
        Assert.Equal(LakeState.Simulating, lake.State);
        Assert.NotNull(lake.RunId);
        Assert.Equal(RunOrigin.LakeWatch, _runs.Items.Single().Origin);
        Assert.Equal(5.0, lake.EstimatedVolumeMcm);
    }
}
