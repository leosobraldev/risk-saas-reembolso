/**
 * Modo demonstração (somente desenvolvimento: `npm run dev:demo`). Dados SINTÉTICOS em memória, sem Auth0 nem API,
 * para revisar o visual e os fluxos. Não entra no build de produção (importado só sob `import.meta.env.DEV`).
 */
import { Auth0Context, initialContext } from '@auth0/auth0-react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter } from 'react-router'
import { ApiProvider } from '@/api/ApiContext'
import { ApiError, type ApiClient } from '@/api/client'
import type { AuditEvent, CurrentTenant, OrderAction, PlatformConnection, RiskEvaluation, RiskLevel } from '@/api/types'
import { AppRoutes } from '@/App'

const minutesAgo = (m: number) => new Date(Date.now() - m * 60_000).toISOString()

const tenant: CurrentTenant = {
  id: '00000000-0000-0000-0000-000000000001',
  name: 'Loja Aurora (demonstração)',
  email: 'dono@aurora.exemplo',
  settings: { highRiskThreshold: 80, autoBlockEnabled: true },
}

const LEVELS: [RiskLevel, number][] = [['High', 92], ['Medium', 61], ['Low', 12], ['High', 85], ['Low', 8], ['Medium', 55], ['Low', 22], ['High', 88], ['Low', 5], ['Medium', 47], ['Low', 15], ['Low', 3]]
const evaluations: RiskEvaluation[] = LEVELS.map(([level, score], i) => ({
  id: `ev-${i}`,
  orderId: `or-${i}`,
  platform: i % 3 === 0 ? 'nuvemshop' : 'shopify',
  externalOrderId: String(4100 + i),
  totalAmount: [259.9, 1480, 89.9, 3290.5, 45, 612.35, 129.9, 899, 39.9, 540, 210, 75.5][i]!,
  score,
  level,
  calculatedAt: minutesAgo(3 + i * 47),
  factors:
    level === 'Low'
      ? []
      : [
          { reason: 'Comprador com reembolsos anteriores', pointsAdded: level === 'High' ? 50 : 30 },
          { reason: 'E-mail de provedor descartável', pointsAdded: level === 'High' ? 35 : 20 },
          ...(score > 90 ? [{ reason: 'Valor muito acima da média da loja', pointsAdded: 10 }] : []),
        ],
}))

let actions: OrderAction[] = evaluations
  .filter((e) => e.level === 'High')
  .map((e, i) => ({
    id: `ac-${i}`,
    evaluationId: e.id,
    orderId: e.orderId,
    platform: e.platform,
    externalOrderId: e.externalOrderId,
    score: e.score,
    type: 'Block' as const,
    status: i === 2 ? ('Failed' as const) : ('Succeeded' as const),
    attempts: i === 2 ? 5 : 1,
    lastError: i === 2 ? 'A loja recusou a requisição após 5 tentativas.' : null,
    createdAt: e.calculatedAt,
    completedAt: e.calculatedAt,
  }))

let connections: PlatformConnection[] = [{ platform: 'shopify', accountId: 'aurora.myshopify.com', hasAccessToken: true, updatedAt: minutesAgo(60 * 30) }]

let audit: AuditEvent[] = [
  { id: 'au-1', occurredAt: minutesAgo(12), actorId: 'auth0|demo', action: 'settings.updated', target: 'settings', details: 'limite 70 -> 80; bloqueio automático desligado -> ligado' },
  { id: 'au-2', occurredAt: minutesAgo(60 * 30), actorId: 'auth0|demo', action: 'connection.upserted', target: 'shopify', details: 'criada; loja aurora.myshopify.com; token de acesso informado' },
  { id: 'au-3', occurredAt: minutesAgo(60 * 31), actorId: 'system', action: 'tenant.registered', target: 'tenant', details: null },
]

const page = <T,>(all: T[], params?: Record<string, string | number | undefined>) => {
  const size = Number(params?.pageSize ?? 20)
  const current = Number(params?.page ?? 1)
  return { items: all.slice((current - 1) * size, current * size), page: current, pageSize: size, total: all.length }
}

const wait = (ms = 250) => new Promise((resolve) => setTimeout(resolve, ms))

const demoClient: ApiClient = {
  async get(path, params) {
    await wait()
    if (path === '/api/v1/tenants/me') return tenant as never
    if (path === '/api/v1/platforms') {
      return [
        { id: 'shopify', name: 'Shopify', accountIdLabel: 'Domínio da loja', accountIdHint: 'minha-loja.myshopify.com', requiresAccessToken: false, supportsActions: true, webhookSecretHelp: null },
        { id: 'nuvemshop', name: 'Nuvemshop', accountIdLabel: 'Id da loja', accountIdHint: 'Número da loja na Nuvemshop', requiresAccessToken: false, supportsActions: true, webhookSecretHelp: null },
        { id: 'wake', name: 'Wake', accountIdLabel: 'Nome da loja', accountIdHint: 'Nome da loja na Wake', requiresAccessToken: true, supportsActions: false, webhookSecretHelp: 'A Wake não assina o corpo do webhook. Ao inscrever o webhook, cadastre o cabeçalho X-RiskGuard-Token com este mesmo valor.' },
      ] as never
    }
    if (path === '/api/v1/risk/evaluations') return page(evaluations.filter((e) => !params?.level || e.level === params.level), params) as never
    if (path === '/api/v1/actions') return page(actions.filter((a) => !params?.status || a.status === params.status), params) as never
    if (path === '/api/v1/tenants/me/connections') return connections as never
    if (path === '/api/v1/audit-events') return page(audit.filter((e) => !params?.action || e.action === params.action), params) as never
    throw new ApiError(404, 'demo.not_found', path)
  },
  async put(path, body) {
    await wait()
    const input = body as Record<string, unknown>
    if (path === '/api/v1/tenants/me/settings') {
      Object.assign(tenant.settings, body)
      audit = [{ id: crypto.randomUUID(), occurredAt: new Date().toISOString(), actorId: 'auth0|demo', action: 'settings.updated', target: 'settings', details: 'alteração feita no modo demonstração' }, ...audit]
      return body as never
    }
    const platform = path.split('/').pop()!
    const next: PlatformConnection = { platform, accountId: String(input.accountId), hasAccessToken: Boolean(input.accessToken), updatedAt: new Date().toISOString() }
    connections = [...connections.filter((c) => c.platform !== platform), next]
    return next as never
  },
  async post(path) {
    await wait()
    const match = /actions\/(.+)\/release$/.exec(path)
    if (match) {
      const id = match[1]!
      actions = actions.map((a) => (a.id === id ? { ...a, status: 'Releasing' as const } : a))
      setTimeout(() => (actions = actions.map((a) => (a.id === id ? { ...a, status: 'Released' as const } : a))), 4_000)
      return actions.find((a) => a.id === id) as never
    }
    return undefined as never
  },
  async delete(path) {
    await wait()
    const platform = path.split('/').pop()!
    connections = connections.filter((c) => c.platform !== platform)
  },
}

const auth = {
  ...initialContext,
  isLoading: false,
  isAuthenticated: true,
  user: { name: 'Demonstração' },
  loginWithRedirect: async () => {},
  logout: async () => {},
  getAccessTokenSilently: (async () => 'demo') as never,
}

export function DemoApp() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { refetchOnWindowFocus: false, retry: false } } })
  return (
    <Auth0Context.Provider value={auth}>
      <QueryClientProvider client={queryClient}>
        <ApiProvider client={demoClient}>
          <BrowserRouter>
            <p className="bg-line-yellow px-4 py-1 text-center text-xs font-semibold text-ink">Modo demonstração: dados sintéticos, nada é enviado.</p>
            <AppRoutes />
          </BrowserRouter>
        </ApiProvider>
      </QueryClientProvider>
    </Auth0Context.Provider>
  )
}
