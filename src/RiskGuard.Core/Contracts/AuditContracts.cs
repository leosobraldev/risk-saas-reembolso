using RiskGuard.Core.Abstractions;

namespace RiskGuard.Core.Contracts;

/// <summary>Nomes estaveis das acoes auditadas (contrato com quem consulta a trilha; nao renomear).</summary>
public static class AuditActions
{
    public const string TenantRegistered = "tenant.registered";
    public const string TenantDeactivated = "tenant.deactivated";
    public const string SettingsUpdated = "settings.updated";
    public const string ConnectionUpserted = "connection.upserted";
    public const string ConnectionRemoved = "connection.removed";
    public const string CustomerAnonymizationRequested = "privacy.anonymization_requested";
    public const string ActionReleaseRequested = "action.release_requested";
}

/// <summary>
/// Registra um evento na trilha de auditoria do lojista (imutavel, tabela do modulo Tenant). Qualquer modulo pode enviar; quem
/// guarda e o Tenant. <c>Details</c> e texto curto escrito PELO CODIGO (ex.: "limite 80 -> 70"): nunca segredos, e-mails
/// de compradores nem outros dados pessoais. O ator vem do contexto da requisicao ("system" fora dela).
/// </summary>
public sealed record RecordAuditEventCommand(Guid TenantId, string Action, string? Target = null, string? Details = null) : ICommand<Result>;
