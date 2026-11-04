import { createContext, use } from 'react'
import type { ApiClient } from './client'

export const ApiContext = createContext<ApiClient | null>(null)

export function useApi(): ApiClient {
  const client = use(ApiContext)
  if (!client) throw new Error('useApi precisa estar dentro de <ApiProvider>.')
  return client
}
