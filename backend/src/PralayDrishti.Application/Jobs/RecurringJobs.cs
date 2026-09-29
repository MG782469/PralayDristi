using PralayDrishti.Application.Common.Abstractions;

namespace PralayDrishti.Application.Jobs;

/// <summary>The one place recurring schedules are declared; Infrastructure calls this at startup.</summary>
public static class RecurringJobs
{
    public const string RefreshTokenCleanup = "refresh-token-cleanup";

    public static void Register(IBackgroundJobScheduler scheduler)
    {
        scheduler.AddOrUpdateRecurring<RefreshTokenCleanupJob>(RefreshTokenCleanup, j => j.Execute(), "0 3 * * *");
    }
}
