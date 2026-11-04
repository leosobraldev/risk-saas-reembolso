using System.Net;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using RiskGuard.Core.Contracts;
using RiskGuard.Infrastructure.ExternalApi;
using RiskGuard.IntegrationTests.Infrastructure;
using RiskGuard.Modules.Action.Jobs;
using static RiskGuard.IntegrationTests.Actions.ActionTestHarness;
using static RiskGuard.IntegrationTests.Risk.RiskTestHarness;
using static RiskGuard.IntegrationTests.Tenant.TenantTestHarness;

namespace RiskGuard.IntegrationTests.Actions;

/// <summary>
/// RNF04 sem esperar os backoffs reais do Hangfire: o servidor fica desligado e os "retries" sao as execucoes
/// sucessivas do mesmo job, exatamente como o Hangfire as faria.
/// </summary>
[Collection(InfrastructureCollection.Name)]
public class OrderActionRetryTests(InfrastructureFixture infrastructure)
{
    private const int MaxRetries = 2;

    [Fact]
    public async Task Duplicate_risk_events_for_the_same_evaluation_create_a_single_action()
    {
        await using var factory = new ApiFactory(infrastructure, hangfireServerEnabled: false);
        var (tenantId, _) = await RegisterAsync(factory);
        await ConfigureTenantAsync(infrastructure, tenantId, autoBlock: true);
        var evt = HighRisk(tenantId, NewExternalId());

        await PublishAsync(factory, evt);
        await PublishAsync(factory, evt);

        (await CountActionsAsync(infrastructure, tenantId)).Should().Be(1);
    }

    [Fact]
    public async Task Transient_failure_is_recorded_then_a_later_attempt_succeeds_and_further_runs_do_nothing()
    {
        await using var factory = new ApiFactory(infrastructure, hangfireServerEnabled: false, hangfireMaxRetries: MaxRetries);
        var (tenantId, _) = await RegisterAsync(factory);
        await ConfigureTenantAsync(infrastructure, tenantId, autoBlock: true);
        var externalId = NewExternalId();
        Fake(factory).Script(externalId, HttpStatusCode.ServiceUnavailable); // 1a chamada: 503
        await PublishAsync(factory, HighRisk(tenantId, externalId));
        var actionId = (await FindByExternalIdAsync(infrastructure, tenantId, externalId))!.Id;

        var firstAttempt = async () => await RunJobAsync(factory, tenantId, actionId);
        await firstAttempt.Should().ThrowAsync<TransientExternalApiException>(); // sobe a excecao: o Hangfire reagenda (RNF04)

        var afterFailure = (await FindByExternalIdAsync(infrastructure, tenantId, externalId))!;
        afterFailure.Status.Should().Be("Pending");
        afterFailure.Attempts.Should().Be(1);
        afterFailure.LastError.Should().Contain("503");
        Fake(factory).ShopifyStatuses(externalId).Should().Equal("open"); // nada foi pausado ainda

        await RunJobAsync(factory, tenantId, actionId); // retry: sucesso
        var callsAfterSuccess = Fake(factory).CallsFor(externalId).Count;
        await RunJobAsync(factory, tenantId, actionId); // reexecucao espuria: ja concluido

        var done = (await FindByExternalIdAsync(infrastructure, tenantId, externalId))!;
        done.Status.Should().Be("Succeeded");
        done.LastError.Should().BeNull();
        Fake(factory).ShopifyStatuses(externalId).Should().Equal("on_hold");
        Fake(factory).CallsFor(externalId).Should().HaveCount(callsAfterSuccess); // a 3a execucao nao chamou a plataforma
    }

    [Fact]
    public async Task Exhausting_the_automatic_retries_marks_the_action_failed()
    {
        await using var factory = new ApiFactory(infrastructure, hangfireServerEnabled: false, hangfireMaxRetries: MaxRetries);
        var (tenantId, _) = await RegisterAsync(factory);
        await ConfigureTenantAsync(infrastructure, tenantId, autoBlock: true);
        var externalId = NewExternalId();
        Fake(factory).Script(externalId, Enumerable.Repeat(HttpStatusCode.BadGateway, 10).ToArray());
        await PublishAsync(factory, HighRisk(tenantId, externalId));
        var actionId = (await FindByExternalIdAsync(infrastructure, tenantId, externalId))!.Id;

        for (var run = 1; run <= MaxRetries + 1; run++) // 1 execucao + MaxRetries reexecucoes
        {
            try
            {
                await RunJobAsync(factory, tenantId, actionId);
            }
            catch (TransientExternalApiException)
            {
                // esperado: cada falha transitoria e relancada para o Hangfire
            }
        }

        var failed = (await FindByExternalIdAsync(infrastructure, tenantId, externalId))!;
        failed.Status.Should().Be("Failed");
        failed.Attempts.Should().Be(MaxRetries + 1);
        failed.LastError.Should().Contain("502");

        await RunJobAsync(factory, tenantId, actionId); // ja encerrada: nao chama de novo
        Fake(factory).CallsFor(externalId).Should().HaveCount(MaxRetries + 1);
    }

    [Fact]
    public async Task Skipped_recommendations_are_never_executed()
    {
        await using var factory = new ApiFactory(infrastructure, hangfireServerEnabled: false);
        var (tenantId, _) = await RegisterAsync(factory); // auto-block desligado
        var externalId = NewExternalId();
        await PublishAsync(factory, HighRisk(tenantId, externalId));
        var action = (await FindByExternalIdAsync(infrastructure, tenantId, externalId))!;

        await RunJobAsync(factory, tenantId, action.Id);

        action.Status.Should().Be("Skipped");
        Fake(factory).CallsFor(externalId).Should().BeEmpty();
    }

    [Fact]
    public async Task A_job_for_an_unknown_action_does_not_fail_or_call_out()
    {
        await using var factory = new ApiFactory(infrastructure, hangfireServerEnabled: false);
        var (tenantId, _) = await RegisterAsync(factory);

        var act = async () => await RunJobAsync(factory, tenantId, Guid.NewGuid());

        await act.Should().NotThrowAsync();
        Fake(factory).Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_job_cannot_run_an_action_of_another_tenant()
    {
        await using var factory = new ApiFactory(infrastructure, hangfireServerEnabled: false);
        var (owner, _) = await RegisterAsync(factory);
        var (intruder, _) = await RegisterAsync(factory);
        await ConfigureTenantAsync(infrastructure, owner, autoBlock: true);
        var externalId = NewExternalId();
        await PublishAsync(factory, HighRisk(owner, externalId));
        var actionId = (await FindByExternalIdAsync(infrastructure, owner, externalId))!.Id;

        await RunJobAsync(factory, intruder, actionId); // RLS + filtro: a acao nao e visivel a outro tenant

        Fake(factory).CallsFor(externalId).Should().BeEmpty();
        (await FindByExternalIdAsync(infrastructure, owner, externalId))!.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task A_platform_the_tenant_never_connected_fails_permanently()
    {
        await using var factory = new ApiFactory(infrastructure, hangfireServerEnabled: false);
        var (tenantId, _) = await RegisterAsync(factory, connectPlatforms: false);
        await ConfigureTenantAsync(infrastructure, tenantId, autoBlock: true);
        var externalId = NewExternalId();
        await PublishAsync(factory, HighRisk(tenantId, externalId));
        var actionId = (await FindByExternalIdAsync(infrastructure, tenantId, externalId))!.Id;

        await RunJobAsync(factory, tenantId, actionId);

        var action = (await FindByExternalIdAsync(infrastructure, tenantId, externalId))!;
        action.Status.Should().Be("Failed");
        action.LastError.Should().Contain("nao conectou");
        Fake(factory).Calls.Should().BeEmpty();
    }

    private static RiskCalculatedEvent HighRisk(Guid tenantId, string externalId) =>
        new(tenantId, Guid.NewGuid(), Guid.NewGuid(), "shopify", externalId, Score: 90, RiskLevel.High);

    private static async Task PublishAsync(ApiFactory factory, RiskCalculatedEvent evt)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPublisher>().Publish(evt);
    }

    private static async Task RunJobAsync(ApiFactory factory, Guid tenantId, Guid actionId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ExecuteOrderActionJob>().RunAsync(tenantId, actionId);
    }
}
