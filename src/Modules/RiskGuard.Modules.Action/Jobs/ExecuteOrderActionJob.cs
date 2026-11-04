using MediatR;
using Microsoft.Extensions.DependencyInjection;
using RiskGuard.Modules.Action.Application.Commands;

namespace RiskGuard.Modules.Action.Jobs;

/// <summary>
/// Ponto de entrada do Hangfire. Falhas transitorias sobem como excecao para o retry automatico (RNF04); cada falha e
/// registrada em um escopo novo (o DbContext do escopo falho pode estar inconsistente).
/// </summary>
public sealed class ExecuteOrderActionJob(ISender sender, IServiceScopeFactory scopeFactory)
{
    public async Task RunAsync(Guid tenantId, Guid actionId)
    {
        try
        {
            await sender.Send(new ExecuteOrderActionCommand(tenantId, actionId)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordFailureAsync(tenantId, actionId, ex.Message).ConfigureAwait(false);
            throw;
        }
    }

    private async Task RecordFailureAsync(Guid tenantId, Guid actionId, string error)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(new RecordActionFailureCommand(tenantId, actionId, error)).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // melhor esforco: a excecao original (relancada pelo chamador) e a que importa ao retry
        catch (Exception)
#pragma warning restore CA1031
        {
            // banco indisponivel: o retry do Hangfire tentara novamente
        }
    }
}
