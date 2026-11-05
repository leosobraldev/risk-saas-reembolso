/** Contratos da API RiskGuard (espelham os DTOs de src/ em C#; JSON em camelCase). */

export type RiskLevel = 'Low' | 'Medium' | 'High'

export interface Page<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}

export interface RiskFactor {
  reason: string
  pointsAdded: number
}

export interface RiskEvaluation {
  id: string
  orderId: string
  platform: string
  externalOrderId: string
  totalAmount: number
  score: number
  level: RiskLevel
  calculatedAt: string
  factors: RiskFactor[]
}

export type OrderActionStatus =
  | 'Pending'
  | 'Succeeded'
  | 'Failed'
  | 'Skipped'
  | 'Releasing'
  | 'Released'
  | 'ReleaseFailed'

export type OrderActionType = 'Block' | 'Flag'

export interface OrderAction {
  id: string
  evaluationId: string
  orderId: string
  platform: string
  externalOrderId: string
  score: number
  type: OrderActionType
  status: OrderActionStatus
  attempts: number
  lastError: string | null
  createdAt: string
  completedAt: string | null
}

export interface TenantSettings {
  highRiskThreshold: number
  autoBlockEnabled: boolean
}

export interface CurrentTenant {
  id: string
  name: string
  email: string
  settings: TenantSettings
}

export interface PlatformConnection {
  platform: string
  accountId: string
  hasAccessToken: boolean
  updatedAt: string
}

export interface AuditEvent {
  id: string
  occurredAt: string
  actorId: string
  action: string
  target: string | null
  details: string | null
}

/** Plataforma de e-commerce suportada (GET /api/v1/platforms): o painel monta o formulário de conexão a partir disto. */
export interface PlatformInfo {
  id: string
  name: string
  accountIdLabel: string
  accountIdHint: string
  /** O webhook traz só o id do pedido: sem o token de acesso o pedido não pode ser buscado. */
  requiresAccessToken: boolean
  /** A plataforma permite reter, sinalizar e liberar pedidos. Falso: só avalia e recomenda. */
  supportsActions: boolean
  webhookSecretHelp: string | null
}
