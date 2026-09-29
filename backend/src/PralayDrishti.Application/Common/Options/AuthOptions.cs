namespace PralayDrishti.Application.Common.Options;

public sealed class AuthOptions
{
    public const string Section = "Auth";

    /// <summary>On for the demo; production turns it off and users come from an Admin or agency SSO.</summary>
    public bool AllowSelfRegistration { get; set; } = true;
}

public sealed class HangfireJobOptions
{
    public const string Section = "Hangfire";
    public int SimulationWorkers { get; set; } = 1;
    public string DashboardPath { get; set; } = "/hangfire";
}
