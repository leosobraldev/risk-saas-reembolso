import { ApiError, createApiClient } from './client'

const json = (body: unknown, init: ResponseInit = {}) =>
  new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' }, ...init })

function setup(fetchImpl: typeof fetch, getToken: () => Promise<string> = async () => 'tok-123') {
  return createApiClient({ baseUrl: 'https://api.test', getToken, fetchImpl })
}

describe('createApiClient', () => {
  it('envia o token de acesso em Authorization e os parâmetros preenchidos', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(json({ ok: true }))

    await setup(fetchImpl).get('/api/v1/actions', { page: 2, pageSize: 20, status: undefined, level: '' })

    const [url, init] = fetchImpl.mock.calls[0]!
    expect(String(url)).toBe('https://api.test/api/v1/actions?page=2&pageSize=20')
    expect((init as RequestInit).headers).toMatchObject({ Authorization: 'Bearer tok-123' })
  })

  it('pede o token a cada chamada (não guarda o token)', async () => {
    const getToken = vi.fn().mockResolvedValue('t')
    const client = setup(vi.fn().mockImplementation(async () => json({})), getToken)

    await client.get('/a')
    await client.get('/b')

    expect(getToken).toHaveBeenCalledTimes(2)
  })

  it('envia JSON no PUT e devolve o corpo', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(json({ highRiskThreshold: 70, autoBlockEnabled: true }))

    const result = await setup(fetchImpl).put('/api/v1/tenants/me/settings', { highRiskThreshold: 70, autoBlockEnabled: true })

    const init = fetchImpl.mock.calls[0]![1] as RequestInit
    expect(init.method).toBe('PUT')
    expect((init.headers as Record<string, string>)['Content-Type']).toBe('application/json')
    expect(init.body).toBe('{"highRiskThreshold":70,"autoBlockEnabled":true}')
    expect(result).toEqual({ highRiskThreshold: 70, autoBlockEnabled: true })
  })

  it('trata 204 como sucesso sem corpo', async () => {
    const client = setup(vi.fn().mockResolvedValue(new Response(null, { status: 204 })))

    await expect(client.delete('/api/v1/tenants/me/connections/shopify')).resolves.toBeUndefined()
  })

  it('converte ProblemDetails em ApiError com status e code estável', async () => {
    const client = setup(vi.fn().mockResolvedValue(json({ title: 'Acao nao encontrada.', code: 'action.not_found' }, { status: 404 })))

    const error = await client.post('/api/v1/actions/x/release').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ status: 404, code: 'action.not_found' })
  })

  it('lê Retry-After do 429', async () => {
    const client = setup(vi.fn().mockResolvedValue(json({ code: 'rate.limited' }, { status: 429, headers: { 'Retry-After': '30' } })))

    const error = (await client.get('/x').catch((e: unknown) => e)) as ApiError

    expect(error.status).toBe(429)
    expect(error.retryAfterSeconds).toBe(30)
  })

  it('erro sem corpo JSON (ex.: 502 do proxy) vira mensagem genérica, sem vazar o corpo', async () => {
    const client = setup(vi.fn().mockResolvedValue(new Response('<html>bad gateway interno</html>', { status: 502 })))

    const error = (await client.get('/x').catch((e: unknown) => e)) as ApiError

    expect(error.status).toBe(502)
    expect(error.code).toBe('http.error')
    expect(error.message).not.toContain('bad gateway')
  })

  it('falha de rede vira network.unreachable (status 0)', async () => {
    const client = setup(vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))

    await expect(client.get('/x')).rejects.toMatchObject({ status: 0, code: 'network.unreachable' })
  })

  it('sem conseguir o token, não chama a API e pede nova entrada', async () => {
    const fetchImpl = vi.fn()
    const client = setup(fetchImpl, async () => {
      throw new Error('login_required')
    })

    await expect(client.get('/x')).rejects.toMatchObject({ status: 401, code: 'auth.session_expired' })
    expect(fetchImpl).not.toHaveBeenCalled()
  })
})
