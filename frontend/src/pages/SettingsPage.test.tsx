import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ApiError } from '@/api/client'
import { connection, tenant } from '@/test/fixtures'
import { fakeClient, renderWithApp } from '@/test/render'
import { SettingsPage } from './SettingsPage'

function clientWith(connections = [connection()], overrides: Partial<ReturnType<typeof fakeClient>> = {}) {
  return fakeClient({
    get: vi.fn().mockImplementation(async (path: string) => (path === '/api/v1/tenants/me' ? tenant : connections)),
    ...overrides,
  })
}

describe('SettingsPage: regras de risco', () => {
  it('mostra os valores atuais e só habilita salvar quando algo muda', async () => {
    renderWithApp(<SettingsPage />, clientWith())

    const threshold = await screen.findByLabelText('Limite de risco alto (0 a 100)')
    const save = screen.getByRole('button', { name: 'Salvar regras' })
    expect(threshold).toHaveValue(80)
    expect(screen.getByRole('switch')).not.toBeChecked()
    expect(save).toBeDisabled()

    await userEvent.clear(threshold)
    await userEvent.type(threshold, '70')

    expect(save).toBeEnabled()
  })

  it('salva o limite e o bloqueio automático', async () => {
    const put = vi.fn().mockResolvedValue({ highRiskThreshold: 65, autoBlockEnabled: true })
    renderWithApp(<SettingsPage />, clientWith([], { put }))
    const threshold = await screen.findByLabelText('Limite de risco alto (0 a 100)')

    await userEvent.clear(threshold)
    await userEvent.type(threshold, '65')
    await userEvent.click(screen.getByRole('switch'))
    await userEvent.click(screen.getByRole('button', { name: 'Salvar regras' }))

    await waitFor(() => expect(put).toHaveBeenCalledWith('/api/v1/tenants/me/settings', { highRiskThreshold: 65, autoBlockEnabled: true }))
  })

  it.each(['', '-1', '101', '7.5'])('limite inválido (%j) bloqueia o envio e explica', async (value) => {
    const put = vi.fn()
    renderWithApp(<SettingsPage />, clientWith([], { put }))
    const threshold = await screen.findByLabelText('Limite de risco alto (0 a 100)')

    await userEvent.clear(threshold)
    if (value) await userEvent.type(threshold, value)

    expect(screen.getByRole('button', { name: 'Salvar regras' })).toBeDisabled()
    expect(screen.getByText('Informe um número inteiro de 0 a 100.')).toBeInTheDocument()
    expect(put).not.toHaveBeenCalled()
  })

  it('erro da API aparece em português', async () => {
    const put = vi.fn().mockRejectedValue(new ApiError(503, 'x', 'x'))
    renderWithApp(<SettingsPage />, clientWith([], { put }))

    await userEvent.click(await screen.findByRole('switch'))
    await userEvent.click(screen.getByRole('button', { name: 'Salvar regras' }))

    expect(await screen.findByText('O RiskGuard está indisponível no momento. Tente novamente em instantes.')).toBeInTheDocument()
  })
})

describe('SettingsPage: lojas conectadas', () => {
  it('mostra a loja conectada e a que falta conectar, sem nenhum segredo', async () => {
    renderWithApp(<SettingsPage />, clientWith([connection()]))

    expect(await screen.findByDisplayValue('aurora.myshopify.com')).toBeInTheDocument()
    expect(screen.getByText(/Conectada · atualizada/)).toBeInTheDocument()
    expect(screen.getByText('Não conectada')).toBeInTheDocument()
    expect(screen.getByText('(já informado)')).toBeInTheDocument()
    for (const field of screen.getAllByLabelText(/Segredo do webhook|Token de acesso/)) {
      expect(field).toHaveValue('')
      expect(field).toHaveAttribute('type', 'password')
    }
  })

  it('conecta uma loja enviando os segredos e depois limpa os campos', async () => {
    const put = vi.fn().mockResolvedValue({})
    renderWithApp(<SettingsPage />, clientWith([], { put }))
    const nuvemshop = (await screen.findByRole('heading', { name: 'Nuvemshop' })).closest('form')!

    await userEvent.type(within(nuvemshop).getByLabelText('Id da loja'), '12345')
    await userEvent.type(within(nuvemshop).getByLabelText('Segredo do webhook'), 'segredo-secreto')
    await userEvent.type(within(nuvemshop).getByLabelText(/Token de acesso/), 'token-secreto')
    await userEvent.click(within(nuvemshop).getByRole('button', { name: 'Conectar' }))

    await waitFor(() =>
      expect(put).toHaveBeenCalledWith('/api/v1/tenants/me/connections/nuvemshop', {
        accountId: '12345',
        webhookSecret: 'segredo-secreto',
        accessToken: 'token-secreto',
      }),
    )
    await waitFor(() => expect(within(nuvemshop).getByLabelText('Segredo do webhook')).toHaveValue(''))
    expect(within(nuvemshop).getByLabelText(/Token de acesso/)).toHaveValue('')
    expect(screen.queryByDisplayValue('segredo-secreto')).not.toBeInTheDocument()
  })

  it('não deixa conectar sem identificador da loja ou sem segredo do webhook', async () => {
    renderWithApp(<SettingsPage />, clientWith([]))
    const shopify = (await screen.findByRole('heading', { name: 'Shopify' })).closest('form')!
    const connect = within(shopify).getByRole('button', { name: 'Conectar' })
    expect(connect).toBeDisabled()

    await userEvent.type(within(shopify).getByLabelText('Domínio da loja'), 'aurora.myshopify.com')
    expect(connect).toBeDisabled()

    await userEvent.type(within(shopify).getByLabelText('Segredo do webhook'), 'x')
    expect(connect).toBeEnabled()
  })

  it('mostra o erro em português quando a loja é recusada', async () => {
    const put = vi.fn().mockRejectedValue(new ApiError(400, 'connection.invalid_account', 'x'))
    renderWithApp(<SettingsPage />, clientWith([], { put }))
    const shopify = (await screen.findByRole('heading', { name: 'Shopify' })).closest('form')!

    await userEvent.type(within(shopify).getByLabelText('Domínio da loja'), 'invalido')
    await userEvent.type(within(shopify).getByLabelText('Segredo do webhook'), 'x')
    await userEvent.click(within(shopify).getByRole('button', { name: 'Conectar' }))

    expect(await within(shopify).findByRole('alert')).toHaveTextContent('Confira o identificador da loja')
  })

  it('desconectar pede confirmação e só então chama a API', async () => {
    const remove = vi.fn().mockResolvedValue(undefined)
    renderWithApp(<SettingsPage />, clientWith([connection()], { delete: remove }))

    await userEvent.click(await screen.findByRole('button', { name: 'Desconectar' }))
    const dialog = await screen.findByRole('alertdialog')
    expect(remove).not.toHaveBeenCalled()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Desconectar' }))

    await waitFor(() => expect(remove).toHaveBeenCalledWith('/api/v1/tenants/me/connections/shopify'))
  })
})

describe('SettingsPage: privacidade', () => {
  it('exige e-mail válido e confirmação antes de eliminar os dados do comprador', async () => {
    const post = vi.fn().mockResolvedValue(undefined)
    renderWithApp(<SettingsPage />, clientWith([], { post }))
    const email = await screen.findByLabelText('E-mail do comprador')
    const trigger = screen.getByRole('button', { name: 'Eliminar dados do comprador' })
    expect(trigger).toBeDisabled()

    await userEvent.type(email, 'invalido')
    expect(trigger).toBeDisabled()
    await userEvent.clear(email)
    await userEvent.type(email, 'ana@exemplo.com')
    await userEvent.click(trigger)
    const dialog = await screen.findByRole('alertdialog')
    expect(post).not.toHaveBeenCalled()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Eliminar dados' }))

    await waitFor(() => expect(post).toHaveBeenCalledWith('/api/v1/privacy/customers/anonymize', { email: 'ana@exemplo.com' }))
    expect(await screen.findByText('Pedido de eliminação registrado.')).toBeInTheDocument()
    await waitFor(() => expect(email).toHaveValue(''))
  })
})
