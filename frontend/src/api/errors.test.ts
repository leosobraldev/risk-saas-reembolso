import { ApiError } from './client'
import { errorMessage, isRetryable } from './errors'

describe('errorMessage', () => {
  it('usa o texto em português do código conhecido, não o texto técnico do servidor', () => {
    const message = errorMessage(new ApiError(409, 'action.not_releasable', 'Uma acao em Released nao pode ser liberada'))

    expect(message).toBe('Esta ação não pode mais ser liberada: só ações aplicadas com sucesso podem ser.')
  })

  it('403 sem código conhecido é tratado como loja sem acesso', () => {
    expect(errorMessage(new ApiError(403, 'x', 'proibido'))).toContain('desativada')
  })

  it('429 informa quanto esperar', () => {
    expect(errorMessage(new ApiError(429, 'rate.limited', 'x', 30))).toContain('30 segundos')
    expect(errorMessage(new ApiError(429, 'rate.limited', 'x'))).toContain('Aguarde um instante')
  })

  it('5xx e erros desconhecidos têm mensagens próprias e nunca expõem detalhes', () => {
    expect(errorMessage(new ApiError(503, 'x', 'SELECT * FROM'))).toBe('O RiskGuard está indisponível no momento. Tente novamente em instantes.')
    expect(errorMessage(new Error('stack interna'))).toBe('Não foi possível concluir a operação. Tente novamente.')
  })
})

describe('errorMessage: conexões', () => {
  it('token de acesso obrigatório tem mensagem própria', () => {
    expect(errorMessage(new ApiError(400, 'connection.access_token_required', 'x'))).toContain('exige o token de acesso')
  })
})

describe('isRetryable', () => {
  it.each([
    [new ApiError(0, 'network.unreachable', ''), true],
    [new ApiError(503, 'x', ''), true],
    [new ApiError(429, 'x', ''), true],
    [new ApiError(400, 'x', ''), false],
    [new ApiError(401, 'x', ''), false],
    [new ApiError(403, 'x', ''), false],
    [new ApiError(404, 'x', ''), false],
    [new Error('inesperado'), true],
  ])('%o => %s', (error, expected) => {
    expect(isRetryable(error)).toBe(expected)
  })
})
