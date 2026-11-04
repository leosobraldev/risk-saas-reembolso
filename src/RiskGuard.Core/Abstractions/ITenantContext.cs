namespace RiskGuard.Core.Abstractions;

/// <summary>Tenant da requisicao/job corrente. Alimenta o Global Query Filter do EF Core.</summary>
public interface ITenantContext
{
    Guid? TenantId { get; }

    bool HasTenant => TenantId.HasValue;
}

/// <summary>Entidade isolada por tenant (filtrada por <c>TenantId</c>).</summary>
public interface ITenantEntity
{
    Guid TenantId { get; }
}
