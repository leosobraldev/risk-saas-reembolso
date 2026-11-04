using RiskGuard.Core.Abstractions;
using RiskGuard.Modules.Action;
using RiskGuard.Modules.Ingestion;
using RiskGuard.Modules.Risk;
using RiskGuard.Modules.Tenant;

namespace RiskGuard.Api;

/// <summary>Unico lugar que conhece todos os modulos (composition root). Modulos nunca se referenciam entre si.</summary>
internal static class ModuleCatalog
{
    public static IReadOnlyList<IModule> All { get; } =
    [
        new TenantModule(),
        new IngestionModule(),
        new RiskModule(),
        new ActionModule(),
    ];
}
