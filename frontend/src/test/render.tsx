import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import type { ReactElement } from 'react'
import { MemoryRouter } from 'react-router'
import { ApiProvider } from '@/api/ApiContext'
import type { ApiClient } from '@/api/client'

export function fakeClient(overrides: Partial<ApiClient> = {}): ApiClient & { get: ReturnType<typeof vi.fn>; put: ReturnType<typeof vi.fn>; post: ReturnType<typeof vi.fn>; delete: ReturnType<typeof vi.fn> } {
  return {
    get: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 }),
    put: vi.fn().mockResolvedValue({}),
    post: vi.fn().mockResolvedValue(undefined),
    delete: vi.fn().mockResolvedValue(undefined),
    ...overrides,
  } as never
}

/** Renderiza dentro dos mesmos provedores do app (rota, React Query sem retry/cache, cliente de API falso). */
export function renderWithApp(ui: ReactElement, client: ApiClient = fakeClient(), route = '/') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[route]}>
      <QueryClientProvider client={queryClient}>
        <ApiProvider client={client}>{ui}</ApiProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}
