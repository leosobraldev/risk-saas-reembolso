using RiskGuard.Core.Abstractions;

namespace RiskGuard.Modules.Tenant.Domain;

/// <summary>Configuracoes de risco do lojista (1:1 com <see cref="Tenant"/>). Protegida por Global Query Filter e RLS.</summary>
public sealed class TenantSettings : ITenantEntity
{
    public const int DefaultHighRiskThreshold = 80;

    private TenantSettings()
    {
        // EF Core
    }

    public Guid TenantId { get; private set; }

    /// <summary>Score acima do qual o pedido e considerado de risco Alto (0 a 100).</summary>
    public int HighRiskThreshold { get; private set; }

    /// <summary>Quando ligado, o ActionModule pausa automaticamente pedidos de risco Alto. Desligado por padrao.</summary>
    public bool AutoBlockEnabled { get; private set; }

    /// <summary>Altera as configuracoes de risco. O limite deve estar entre 0 e 100 (a escala do score).</summary>
    public Result Update(int highRiskThreshold, bool autoBlockEnabled)
    {
        if (highRiskThreshold is < 0 or > 100)
        {
            return Result.Failure(new Error("settings.invalid_threshold", "O limite de risco alto deve estar entre 0 e 100.", ErrorType.Validation));
        }

        HighRiskThreshold = highRiskThreshold;
        AutoBlockEnabled = autoBlockEnabled;
        return Result.Success();
    }

    public static TenantSettings CreateDefault(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId nao pode ser vazio.", nameof(tenantId));
        }

        return new TenantSettings
        {
            TenantId = tenantId,
            HighRiskThreshold = DefaultHighRiskThreshold,
            AutoBlockEnabled = false,
        };
    }
}
