/** Configuracao publica do app (injetada pelo Vite em build). Nenhum valor aqui e segredo. */
function required(name: string, value: string | undefined): string {
  if (!value || value.trim() === '') {
    throw new Error(`Variavel de ambiente ${name} nao configurada. Veja frontend/.env.example.`)
  }
  return value.trim()
}

export interface AppConfig {
  apiUrl: string
  auth0Domain: string
  auth0ClientId: string
  auth0Audience: string
}

export function loadConfig(env: Record<string, string | undefined> = import.meta.env): AppConfig {
  return {
    apiUrl: required('VITE_API_URL', env.VITE_API_URL).replace(/\/+$/, ''),
    auth0Domain: required('VITE_AUTH0_DOMAIN', env.VITE_AUTH0_DOMAIN),
    auth0ClientId: required('VITE_AUTH0_CLIENT_ID', env.VITE_AUTH0_CLIENT_ID),
    auth0Audience: required('VITE_AUTH0_AUDIENCE', env.VITE_AUTH0_AUDIENCE),
  }
}
