import type { AuditEvent, CurrentTenant, OrderAction, Page, PlatformConnection, RiskEvaluation } from '@/api/types'

export const page = <T>(items: T[], total = items.length, pageNumber = 1): Page<T> => ({ items, page: pageNumber, pageSize: 20, total })

export const tenant: CurrentTenant = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Loja Aurora',
  email: 'dono@aurora.com.br',
  settings: { highRiskThreshold: 80, autoBlockEnabled: false },
}

export const evaluation = (overrides: Partial<RiskEvaluation> = {}): RiskEvaluation => ({
  id: 'ev-1',
  orderId: 'or-1',
  platform: 'shopify',
  externalOrderId: '1001',
  totalAmount: 259.9,
  score: 85,
  level: 'High',
  calculatedAt: new Date(Date.now() - 5 * 60_000).toISOString(),
  factors: [
    { reason: 'Reembolsos anteriores do comprador', pointsAdded: 50 },
    { reason: 'E-mail descartável', pointsAdded: 35 },
  ],
  ...overrides,
})

export const action = (overrides: Partial<OrderAction> = {}): OrderAction => ({
  id: 'ac-1',
  evaluationId: 'ev-1',
  orderId: 'or-1',
  platform: 'shopify',
  externalOrderId: '1001',
  score: 85,
  type: 'Block',
  status: 'Succeeded',
  attempts: 1,
  lastError: null,
  createdAt: new Date(Date.now() - 10 * 60_000).toISOString(),
  completedAt: new Date(Date.now() - 9 * 60_000).toISOString(),
  ...overrides,
})

export const connection = (overrides: Partial<PlatformConnection> = {}): PlatformConnection => ({
  platform: 'shopify',
  accountId: 'aurora.myshopify.com',
  hasAccessToken: true,
  updatedAt: new Date(Date.now() - 2 * 3_600_000).toISOString(),
  ...overrides,
})

export const auditEvent = (overrides: Partial<AuditEvent> = {}): AuditEvent => ({
  id: 'au-1',
  occurredAt: new Date(Date.now() - 60_000).toISOString(),
  actorId: 'auth0|abc',
  action: 'settings.updated',
  target: 'settings',
  details: 'limite 80 -> 65; bloqueio automático desligado -> ligado',
  ...overrides,
})
