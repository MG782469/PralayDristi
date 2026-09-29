using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Exceptions;

namespace PralayDrishti.Domain.Entities;

public class SimulationRun
{
    private SimulationRun() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public RunStatus Status { get; private set; }
    public double Progress { get; private set; }
    public string Stage { get; private set; } = "";
    public string Mode { get; private set; } = "";
    public RunOrigin Origin { get; private set; }
    public string SpecJson { get; private set; } = "{}";
    public string? SummaryJson { get; private set; }
    public string? Error { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime? StartedUtc { get; private set; }
    public DateTime? FinishedUtc { get; private set; }
    public int? DamId { get; private set; }
    public Dam? Dam { get; private set; }
    public Guid? LakeDetectionId { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public User? CreatedBy { get; private set; }
    public string? JobId { get; private set; }

    public static SimulationRun Queue(string name, string mode, string specJson, RunOrigin origin, DateTime nowUtc,
        int? damId = null, Guid? lakeId = null, Guid? userId = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name.Length > 160 ? name[..160] : name,
        Mode = mode,
        SpecJson = specJson,
        Origin = origin,
        Status = RunStatus.Queued,
        Stage = "Queued",
        CreatedUtc = nowUtc,
        DamId = damId,
        LakeDetectionId = lakeId,
        CreatedByUserId = userId
    };

    public bool IsFinished => Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled;

    public void AttachJob(string jobId) => JobId = jobId;

    public void Start(DateTime nowUtc)
    {
        if (IsFinished) throw new BusinessRuleException($"Run {Id} is already {Status}.");
        Status = RunStatus.Running;
        StartedUtc = nowUtc;
        Stage = "Starting";
    }

    public void ReportProgress(string stage, double fraction)
    {
        Stage = stage.Length > 120 ? stage[..120] : stage;
        Progress = Math.Clamp(fraction, 0, 1);
    }

    public void Complete(string summaryJson, DateTime nowUtc)
    {
        Status = RunStatus.Completed;
        SummaryJson = summaryJson;
        Progress = 1;
        Stage = "Done";
        FinishedUtc = nowUtc;
    }

    public void Fail(string error, DateTime nowUtc)
    {
        Status = RunStatus.Failed;
        Error = error.Length > 2000 ? error[..2000] : error;
        Stage = "Failed";
        FinishedUtc = nowUtc;
    }

    public void Cancel(DateTime nowUtc)
    {
        if (IsFinished) throw new BusinessRuleException($"Run {Id} is already {Status}.");
        Status = RunStatus.Cancelled;
        Stage = "Cancelled";
        FinishedUtc = nowUtc;
    }
}
