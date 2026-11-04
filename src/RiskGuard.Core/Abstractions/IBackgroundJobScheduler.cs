using System.Linq.Expressions;

namespace RiskGuard.Core.Abstractions;

/// <summary>Enfileira trabalho assincrono (Hangfire na infraestrutura) sem acoplar os modulos ao Hangfire.</summary>
public interface IBackgroundJobScheduler
{
    /// <returns>Identificador do job enfileirado.</returns>
    string Enqueue<TJob>(Expression<Func<TJob, Task>> methodCall);

    /// <summary>Registra (ou atualiza) um job recorrente. <paramref name="cron"/> em UTC.</summary>
    void AddRecurring<TJob>(string jobId, Expression<Func<TJob, Task>> methodCall, string cron);
}
