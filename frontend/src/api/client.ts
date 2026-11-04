/** Erro de API com o `code` estável do ProblemDetails (RFC 9457) que o backend devolve. */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly retryAfterSeconds: number | null

  constructor(status: number, code: string, message: string, retryAfterSeconds: number | null = null) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.retryAfterSeconds = retryAfterSeconds
  }
}

export interface ApiClient {
  get<T>(path: string, params?: Record<string, string | number | undefined>): Promise<T>
  put<T>(path: string, body: unknown): Promise<T>
  post<T>(path: string, body?: unknown): Promise<T>
  delete(path: string): Promise<void>
}

interface ClientOptions {
  baseUrl: string
  /** Token de acesso do usuario (Auth0). O app nunca guarda o token: pede a cada chamada ao SDK. */
  getToken: () => Promise<string>
  fetchImpl?: typeof fetch
}

const GENERIC_MESSAGE = 'Não foi possível concluir a operação. Tente novamente.'

async function toApiError(response: Response): Promise<ApiError> {
  let code = 'http.error'
  let message = GENERIC_MESSAGE
  try {
    const problem = (await response.json()) as { title?: unknown; code?: unknown }
    if (typeof problem.code === 'string') code = problem.code
    if (typeof problem.title === 'string' && problem.title.trim() !== '') message = problem.title
  } catch {
    // corpo vazio ou nao-JSON (ex.: 502 de proxy): fica a mensagem generica
  }
  const retryAfter = Number(response.headers.get('Retry-After'))
  return new ApiError(response.status, code, message, Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : null)
}

export function createApiClient({ baseUrl, getToken, fetchImpl }: ClientOptions): ApiClient {
  async function request<T>(method: string, path: string, body?: unknown, params?: Record<string, string | number | undefined>): Promise<T> {
    const url = new URL(`${baseUrl}${path}`)
    for (const [key, value] of Object.entries(params ?? {})) {
      if (value !== undefined && value !== '') url.searchParams.set(key, String(value))
    }

    let token: string
    try {
      token = await getToken()
    } catch {
      throw new ApiError(401, 'auth.session_expired', 'Sua sessão expirou. Entre novamente.')
    }

    const headers: Record<string, string> = { Authorization: `Bearer ${token}`, Accept: 'application/json' }
    if (body !== undefined) headers['Content-Type'] = 'application/json'

    let response: Response
    try {
      response = await (fetchImpl ?? fetch)(url, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })
    } catch {
      throw new ApiError(0, 'network.unreachable', 'Sem conexão com o RiskGuard. Verifique sua internet e tente novamente.')
    }

    if (!response.ok) throw await toApiError(response)
    if (response.status === 204) return undefined as T
    return (await response.json()) as T
  }

  return {
    get: (path, params) => request('GET', path, undefined, params),
    put: (path, body) => request('PUT', path, body),
    post: (path, body) => request('POST', path, body),
    delete: (path) => request<void>('DELETE', path),
  }
}
