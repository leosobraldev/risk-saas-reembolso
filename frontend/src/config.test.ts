import { loadConfig } from './config'

const valid = {
  VITE_API_URL: 'https://api.riskguard.test/',
  VITE_AUTH0_DOMAIN: 'riskguard.us.auth0.com',
  VITE_AUTH0_CLIENT_ID: 'abc',
  VITE_AUTH0_AUDIENCE: 'https://api.riskguard.app',
}

describe('loadConfig', () => {
  it('lê as variáveis e remove a barra final da URL da API', () => {
    expect(loadConfig(valid)).toEqual({
      apiUrl: 'https://api.riskguard.test',
      auth0Domain: 'riskguard.us.auth0.com',
      auth0ClientId: 'abc',
      auth0Audience: 'https://api.riskguard.app',
    })
  })

  it.each(Object.keys(valid))('falha com mensagem clara quando %s falta', (name) => {
    expect(() => loadConfig({ ...valid, [name]: '  ' })).toThrow(name)
    expect(() => loadConfig({ ...valid, [name]: undefined })).toThrow(name)
  })
})
