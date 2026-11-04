import { Auth0Provider, useAuth0, type AppState } from '@auth0/auth0-react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useMemo, useState, type ReactNode } from 'react'
import { useNavigate } from 'react-router'
import { ApiProvider } from '@/api/ApiContext'
import { createApiClient } from '@/api/client'
import { isRetryable } from '@/api/errors'
import type { AppConfig } from '@/config'

function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 15_000,
        refetchOnWindowFocus: false,
        // repetir 401/403/400 não resolve: só falha de rede e 5xx são tentadas de novo (no máximo 2 vezes)
        retry: (failures, error) => failures < 2 && isRetryable(error),
      },
    },
  })
}

/** Liga o cliente HTTP ao Auth0: o token de acesso é pedido ao SDK a cada chamada e nunca é guardado pelo app. */
function ApiBridge({ config, children }: { config: AppConfig; children: ReactNode }) {
  const { getAccessTokenSilently } = useAuth0()
  const client = useMemo(
    () =>
      createApiClient({
        baseUrl: config.apiUrl,
        getToken: async () => {
          const token = await getAccessTokenSilently({ authorizationParams: { audience: config.auth0Audience } })
          if (!token) throw new Error('Sem token de acesso.')
          return token
        },
      }),
    [config.apiUrl, config.auth0Audience, getAccessTokenSilently],
  )
  return <ApiProvider client={client}>{children}</ApiProvider>
}

export function AuthProviders({ config, children }: { config: AppConfig; children: ReactNode }) {
  const navigate = useNavigate()
  const [queryClient] = useState(createQueryClient)

  const onRedirectCallback = (appState?: AppState) => {
    navigate(appState?.returnTo ?? '/', { replace: true })
  }

  return (
    <Auth0Provider
      domain={config.auth0Domain}
      clientId={config.auth0ClientId}
      authorizationParams={{ audience: config.auth0Audience, redirect_uri: window.location.origin }}
      onRedirectCallback={onRedirectCallback}
      // token só em memória (não fica em localStorage, fora do alcance de XSS que leia o armazenamento);
      // refresh token rotativo mantém a sessão enquanto a aba está aberta
      cacheLocation="memory"
      useRefreshTokens
      useRefreshTokensFallback
    >
      <QueryClientProvider client={queryClient}>
        <ApiBridge config={config}>{children}</ApiBridge>
      </QueryClientProvider>
    </Auth0Provider>
  )
}
