import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useApi } from './context'
import type {
  AuditEvent,
  CurrentTenant,
  OrderAction,
  OrderActionStatus,
  Page,
  PlatformConnection,
  PlatformInfo,
  RiskEvaluation,
  RiskLevel,
  TenantSettings,
} from './types'

export const keys = {
  tenant: ['tenant'] as const,
  evaluations: (page: number, level?: RiskLevel) => ['evaluations', { page, level }] as const,
  evaluationCount: (level: RiskLevel) => ['evaluation-count', level] as const,
  actions: (page: number, status?: OrderActionStatus) => ['actions', { page, status }] as const,
  actionCount: (status: OrderActionStatus) => ['action-count', status] as const,
  platforms: ['platforms'] as const,
  connections: ['connections'] as const,
  audit: (page: number, action?: string) => ['audit', { page, action }] as const,
}

export const PAGE_SIZE = 20

/** Dados do lojista autenticado. Também é a "prova de vida" da sessão: 403 = sem loja ativa. */
export function useTenant() {
  const api = useApi()
  return useQuery({ queryKey: keys.tenant, queryFn: () => api.get<CurrentTenant>('/api/v1/tenants/me'), staleTime: 60_000 })
}

export function useEvaluations(page: number, level?: RiskLevel) {
  const api = useApi()
  return useQuery({
    queryKey: keys.evaluations(page, level),
    queryFn: () => api.get<Page<RiskEvaluation>>('/api/v1/risk/evaluations', { page, pageSize: PAGE_SIZE, level }),
    placeholderData: keepPreviousData,
  })
}

/** Total de avaliações de um nível: lê só o `total` da API (pageSize=1), sem inventar número no cliente. */
export function useEvaluationCount(level: RiskLevel) {
  const api = useApi()
  return useQuery({
    queryKey: keys.evaluationCount(level),
    queryFn: async () => (await api.get<Page<RiskEvaluation>>('/api/v1/risk/evaluations', { page: 1, pageSize: 1, level })).total,
  })
}

export function useActions(page: number, status?: OrderActionStatus) {
  const api = useApi()
  return useQuery({
    queryKey: keys.actions(page, status),
    queryFn: () => api.get<Page<OrderAction>>('/api/v1/actions', { page, pageSize: PAGE_SIZE, status }),
    placeholderData: keepPreviousData,
    // a liberação é assíncrona (job): enquanto houver ação "Liberando", acompanha até concluir
    refetchInterval: (query) => (query.state.data?.items.some((a) => a.status === 'Releasing') ? 3_000 : false),
  })
}

export function useActionCount(status: OrderActionStatus) {
  const api = useApi()
  return useQuery({
    queryKey: keys.actionCount(status),
    queryFn: async () => (await api.get<Page<OrderAction>>('/api/v1/actions', { page: 1, pageSize: 1, status })).total,
  })
}

export function useReleaseAction() {
  const api = useApi()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (actionId: string) => api.post<OrderAction>(`/api/v1/actions/${actionId}/release`),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['actions'] }),
  })
}

export function useUpdateSettings() {
  const api = useApi()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (settings: TenantSettings) => api.put<TenantSettings>('/api/v1/tenants/me/settings', settings),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.tenant }),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['audit'] }),
  })
}

/** Plataformas suportadas e o que cada uma exige. Dado estático do produto: não muda durante a sessão. */
export function usePlatforms() {
  const api = useApi()
  return useQuery({ queryKey: keys.platforms, queryFn: () => api.get<PlatformInfo[]>('/api/v1/platforms'), staleTime: Infinity })
}

export function useConnections() {
  const api = useApi()
  return useQuery({ queryKey: keys.connections, queryFn: () => api.get<PlatformConnection[]>('/api/v1/tenants/me/connections') })
}

export interface ConnectionInput {
  platform: string
  accountId: string
  webhookSecret: string
  accessToken?: string
}

export function useSaveConnection() {
  const api = useApi()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ platform, ...body }: ConnectionInput) => api.put<PlatformConnection>(`/api/v1/tenants/me/connections/${platform}`, body),
    onSettled: () => Promise.all([queryClient.invalidateQueries({ queryKey: keys.connections }), queryClient.invalidateQueries({ queryKey: ['audit'] })]),
  })
}

export function useRemoveConnection() {
  const api = useApi()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (platform: string) => api.delete(`/api/v1/tenants/me/connections/${platform}`),
    onSettled: () => Promise.all([queryClient.invalidateQueries({ queryKey: keys.connections }), queryClient.invalidateQueries({ queryKey: ['audit'] })]),
  })
}

export function useAnonymizeCustomer() {
  const api = useApi()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (email: string) => api.post<void>('/api/v1/privacy/customers/anonymize', { email }),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['audit'] }),
  })
}

export function useAuditEvents(page: number, action?: string) {
  const api = useApi()
  return useQuery({
    queryKey: keys.audit(page, action),
    queryFn: () => api.get<Page<AuditEvent>>('/api/v1/audit-events', { page, pageSize: PAGE_SIZE, action }),
    placeholderData: keepPreviousData,
  })
}
