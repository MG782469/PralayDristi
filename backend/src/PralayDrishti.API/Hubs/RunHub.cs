using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Models;

namespace PralayDrishti.API.Hubs;

/// <summary>
/// Push channel for the dashboard. Server -> client: progress(runId, stage, fraction), status(runId, status),
/// alert(alert), lake(detection).
/// </summary>
[Authorize]
public sealed class RunHub : Hub
{
}

public sealed class SignalRRunNotifier : IRunNotifier
{
    private readonly IHubContext<RunHub> _hub;

    public SignalRRunNotifier(IHubContext<RunHub> hub) => _hub = hub;

    public Task ProgressAsync(Guid runId, string stage, double fraction) => _hub.Clients.All.SendAsync("progress", runId, stage, fraction);
    public Task StatusAsync(Guid runId, string status) => _hub.Clients.All.SendAsync("status", runId, status);
    public Task AlertAsync(AlertDto alert) => _hub.Clients.All.SendAsync("alert", alert);
    public Task LakeAsync(LakeDto lake) => _hub.Clients.All.SendAsync("lake", lake);
}
