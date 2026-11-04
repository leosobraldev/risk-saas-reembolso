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

export const PLATFORMS = ['shopify', 'nuvemshop'] as const
export type Platform = (typeof PLATFORMS)[number]
