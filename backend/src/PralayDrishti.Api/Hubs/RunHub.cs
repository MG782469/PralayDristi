using Microsoft.AspNetCore.SignalR;

namespace PralayDrishti.Api.Hubs;

/// <summary>
/// Push channel for the dashboard. Server -> client messages:
///   progress(runId, stage, fraction), status(runId, status), alert(alert), lake(detection)
/// </summary>
public class RunHub : Hub
{
}
