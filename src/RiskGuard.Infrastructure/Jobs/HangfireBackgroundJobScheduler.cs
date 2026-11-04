using System.Linq.Expressions;
using Hangfire;
using RiskGuard.Core.Abstractions;

namespace RiskGuard.Infrastructure.Jobs;

internal sealed class HangfireBackgroundJobScheduler(IBackgroundJobClient client, IRecurringJobManager recurring) : IBackgroundJobScheduler
{
    public string Enqueue<TJob>(Expression<Func<TJob, Task>> methodCall) => client.Enqueue(methodCall);

    public void AddRecurring<TJob>(string jobId, Expression<Func<TJob, Task>> methodCall, string cron) =>
        recurring.AddOrUpdate(jobId, methodCall, cron, new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
}
