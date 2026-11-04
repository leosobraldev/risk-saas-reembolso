using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using RiskGuard.IntegrationTests.Infrastructure;

namespace RiskGuard.IntegrationTests.Foundation;

[Collection(InfrastructureCollection.Name)]
public class HangfireTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task Enqueued_job_is_persisted_in_postgres_and_executed_by_the_worker()
    {
        await using var factory = new ApiFactory(infrastructure);
        using var _ = factory.CreateClient(); // forca o start do host (e do servidor Hangfire)

        var client = factory.Services.GetRequiredService<IBackgroundJobClient>();
        ProbeJob.Reset();

        var jobId = client.Enqueue<ProbeJob>(job => job.Run());

        var state = await WaitForTerminalStateAsync(factory.Services.GetRequiredService<JobStorage>(), jobId, TimeSpan.FromSeconds(30));
        state.Should().Be("Succeeded");
        ProbeJob.Executions.Should().Be(1);
    }

    [Fact]
    public async Task Failing_job_is_scheduled_for_retry()
    {
        await using var factory = new ApiFactory(infrastructure);
        using var _ = factory.CreateClient();

        var client = factory.Services.GetRequiredService<IBackgroundJobClient>();
        var jobId = client.Enqueue<ProbeJob>(job => job.Fail());

        var state = await WaitForStateAsync(factory.Services.GetRequiredService<JobStorage>(), jobId, "Scheduled", TimeSpan.FromSeconds(30));
        state.Should().Be("Scheduled"); // RNF04: falha => reagendado, nao descartado
    }

    private static async Task<string?> WaitForTerminalStateAsync(JobStorage storage, string jobId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        string? state = null;
        while (DateTime.UtcNow < deadline)
        {
            state = storage.GetMonitoringApi().JobDetails(jobId)?.History.FirstOrDefault()?.StateName;
            if (state is "Succeeded" or "Failed")
            {
                return state;
            }

            await Task.Delay(250);
        }

        return state;
    }

    private static async Task<string?> WaitForStateAsync(JobStorage storage, string jobId, string expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        string? state = null;
        while (DateTime.UtcNow < deadline)
        {
            state = storage.GetMonitoringApi().JobDetails(jobId)?.History.FirstOrDefault()?.StateName;
            if (state == expected)
            {
                return state;
            }

            await Task.Delay(250);
        }

        return state;
    }

    public sealed class ProbeJob
    {
        private static int _executions;

        public static int Executions => Volatile.Read(ref _executions);

        public static void Reset() => Interlocked.Exchange(ref _executions, 0);

        public void Run() => Interlocked.Increment(ref _executions);

        public void Fail() => throw new InvalidOperationException("falha simulada");
    }
}
