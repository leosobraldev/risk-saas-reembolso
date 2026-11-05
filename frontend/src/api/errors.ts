import { ApiError } from './client'

/** Mensagens da interface por código estável da API (o texto do servidor não é exibido: é técnico e sem acentuação). */
const MESSAGES: Record<string, string> = {
  'auth.session_expired': 'Sua sessão expirou. Entre novamente.',
  'auth.no_tenant': 'Sua conta ainda não está vinculada a uma loja. Fale com o suporte do RiskGuard.',
  'tenant.inactive': 'Esta loja está desativada. Fale com o suporte do RiskGuard.',
  'network.unreachable': 'Sem conexão com o RiskGuard. Verifique sua internet e tente novamente.',
  'settings.invalid_request': 'Informe o limite de risco e o bloqueio automático.',
  'action.not_found': 'Esta ação não existe mais.',
  'action.not_releasable': 'Esta ação não pode mais ser liberada: só ações aplicadas com sucesso podem ser.',
  'connection.invalid_account': 'Confira o identificador da loja. Shopify: minha-loja.myshopify.com. Nuvemshop: o id numérico. Wake: o nome da loja.',
  'connection.access_token_required': 'Esta plataforma exige o token de acesso: o webhook traz só o número do pedido e os dados são buscados na API.',
  'connection.invalid_secret': 'Informe o segredo do webhook (e, se houver, o token de acesso), com até 1000 caracteres.',
  'connection.conflict': 'A conexão foi alterada em outro lugar. Tente novamente.',
  'connection.not_found': 'Esta conexão não existe.',
  'privacy.invalid_email': 'Informe um e-mail válido.',
}

export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const known = MESSAGES[error.code]
    if (known) return known
    if (error.status === 429) {
      return error.retryAfterSeconds
        ? `Muitas tentativas. Aguarde ${error.retryAfterSeconds} segundos e tente de novo.`
        : 'Muitas tentativas. Aguarde um instante e tente de novo.'
    }
    if (error.status === 401) return MESSAGES['auth.session_expired']!
    if (error.status === 403) return MESSAGES['tenant.inactive']!
    if (error.status >= 500) return 'O RiskGuard está indisponível no momento. Tente novamente em instantes.'
  }
  return 'Não foi possível concluir a operação. Tente novamente.'
}

/** Erros que repetir não resolve (sessão, permissão, validação): o React Query não deve tentar de novo. */
export function isRetryable(error: unknown): boolean {
  return !(error instanceof ApiError) || error.status === 0 || error.status === 429 || error.status >= 500
}
