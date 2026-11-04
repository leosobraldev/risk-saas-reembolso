import type { OrderActionStatus, OrderActionType, RiskLevel } from '@/api/types'

export const LEVEL_LABEL: Record<RiskLevel, string> = { Low: 'Baixo', Medium: 'Médio', High: 'Alto' }

export const STATUS_LABEL: Record<OrderActionStatus, string> = {
  Pending: 'Na fila',
  Succeeded: 'Aplicada',
  Failed: 'Falhou',
  Skipped: 'Não aplicada',
  Releasing: 'Liberando',
  Released: 'Liberada',
  ReleaseFailed: 'Falha ao liberar',
}

export const ACTION_TYPE_LABEL: Record<OrderActionType, string> = { Block: 'Bloqueio', Flag: 'Sinalização' }

/** Nomes das ações de auditoria em português (os códigos são um contrato estável da API). */
export const AUDIT_ACTIONS: Record<string, string> = {
  'tenant.registered': 'Loja cadastrada',
  'tenant.deactivated': 'Loja desativada',
  'settings.updated': 'Regras de risco alteradas',
  'connection.upserted': 'Loja conectada ou atualizada',
  'connection.removed': 'Loja desconectada',
  'privacy.anonymization_requested': 'Eliminação de dados de comprador',
  'action.release_requested': 'Liberação de pedido',
}

export const actionLabel = (action: string) => AUDIT_ACTIONS[action] ?? action

/** `system` = operação da equipe RiskGuard (cadastro, desativação, rotinas); qualquer outro valor é um usuário da loja. */
export const actorLabel = (actorId: string) => (actorId === 'system' ? 'RiskGuard' : 'Usuário da loja')
