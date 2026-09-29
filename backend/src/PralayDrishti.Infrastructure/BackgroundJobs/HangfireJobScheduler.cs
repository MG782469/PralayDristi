using System.Linq.Expressions;
using Hangfire;
using Hangfire.States;
using PralayDrishti.Application.Common.Abstractions;

namespace PralayDrishti.Infrastructure.BackgroundJobs;

/// <summary>Hangfire behind the Application's scheduler interface; jobs are persisted in Redis and survive restarts.</summary>
public sealed class HangfireJobScheduler : IBackgroundJobScheduler
{
    private readonly IBackgroundJobClient _client;
    private readonly IRecurringJobManager _recurring;

    public HangfireJobScheduler(IBackgroundJobClient client, IRecurringJobManager recurring)
    {
        _client = client;
        _recurring = recurring;
    }

    public string Enqueue<TJob>(Expression<Func<TJob, Task>> call, string queue = "default") =>
        _client.Create(call, new EnqueuedState(queue));

    public bool Delete(string jobId) => _client.Delete(jobId);

    public void AddOrUpdateRecurring<TJob>(string id, Expression<Func<TJob, Task>> call, string cron) =>
        _recurring.AddOrUpdate(id, call, cron, new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
}
