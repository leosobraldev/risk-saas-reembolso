import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ApiError } from '@/api/client'
import { evaluation, page, tenant } from '@/test/fixtures'
import { fakeClient, renderWithApp } from '@/test/render'
import { OrdersPage } from './OrdersPage'

function clientWith(evaluations: ReturnType<typeof page>, extra: Record<string, unknown> = {}) {
  return fakeClient({
    get: vi.fn().mockImplementation(async (path: string, params?: Record<string, unknown>) => {
      if (path === '/api/v1/tenants/me') return tenant
      if (path === '/api/v1/actions') return page([], 3)
      if (path === '/api/v1/risk/evaluations') {
        if (params?.pageSize === 1) return page([], { Low: 40, Medium: 12, High: 5 }[params.level as string] ?? 0)
        return extra.evaluations ?? evaluations
      }
      throw new Error(`rota inesperada ${path}`)
    }),
  })
}

describe('OrdersPage', () => {
  it('lista os pedidos com valor, nível em texto, score e quando foi avaliado', async () => {
    renderWithApp(<OrdersPage />, clientWith(page([evaluation(), evaluation({ id: 'ev-2', externalOrderId: '1002', level: 'Low', score: 10, totalAmount: 49.9 })])))

    const table = await screen.findByRole('table', { name: 'Pedidos avaliados' })
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows).toHaveLength(2)
    expect(within(rows[0]!).getByText('#1001')).toBeInTheDocument()
    expect(within(rows[0]!).getByText('Shopify')).toBeInTheDocument()
    expect(within(rows[0]!).getByText(/259,90/)).toBeInTheDocument()
    expect(within(rows[0]!).getByText('Risco Alto')).toBeInTheDocument()
    expect(within(rows[0]!).getByText('85')).toBeInTheDocument()
    expect(within(rows[0]!).getByText('há 5 minutos')).toBeInTheDocument()
    expect(within(rows[1]!).getByText('Risco Baixo')).toBeInTheDocument()
  })

  it('o trilho do score usa o limite de risco alto do lojista', async () => {
    renderWithApp(<OrdersPage />, clientWith(page([evaluation()])))

    expect(await screen.findByRole('img', { name: 'Score 85 de 100. Limite de risco alto: 80.' })).toBeInTheDocument()
  })

  it('o resumo mostra os totais por nível lidos da API, sem inventar números', async () => {
    renderWithApp(<OrdersPage />, clientWith(page([evaluation()])))

    const summary = await screen.findByRole('region', { name: 'Resumo por nível de risco' })
    await waitFor(() => expect(within(summary).getByRole('button', { name: /Risco Baixo/ })).toHaveTextContent('40'))
    expect(within(summary).getByRole('button', { name: /Risco Médio/ })).toHaveTextContent('12')
    expect(within(summary).getByRole('button', { name: /Risco Alto/ })).toHaveTextContent('5')
    expect(within(summary).getByText('3 para revisar')).toBeInTheDocument()
  })

  it('filtrar por nível pede só aquele nível à API e volta para a página 1', async () => {
    const client = clientWith(page([evaluation()], 45))
    renderWithApp(<OrdersPage />, client)
    await screen.findByRole('table')

    await userEvent.selectOptions(screen.getByLabelText('Nível de risco'), 'High')

    await waitFor(() => expect(client.get).toHaveBeenCalledWith('/api/v1/risk/evaluations', { page: 1, pageSize: 20, level: 'High' }))
  })

  it('clicar numa estação do resumo filtra, e clicar de novo limpa', async () => {
    const client = clientWith(page([evaluation()]))
    renderWithApp(<OrdersPage />, client)
    const summary = await screen.findByRole('region', { name: 'Resumo por nível de risco' })

    await userEvent.click(within(summary).getByRole('button', { name: /Risco Alto/ }))
    await waitFor(() => expect(client.get).toHaveBeenCalledWith('/api/v1/risk/evaluations', { page: 1, pageSize: 20, level: 'High' }))
    expect(screen.getByLabelText('Nível de risco')).toHaveValue('High')

    await userEvent.click(within(summary).getByRole('button', { name: /Risco Alto/ }))
    await waitFor(() => expect(screen.getByLabelText('Nível de risco')).toHaveValue(''))
  })

  it('expandir uma linha mostra por que o pedido recebeu aquele score', async () => {
    renderWithApp(<OrdersPage />, clientWith(page([evaluation()])))

    await userEvent.click(await screen.findByRole('button', { name: 'Ver motivos do pedido 1001' }))

    expect(screen.getByText('Reembolsos anteriores do comprador')).toBeInTheDocument()
    expect(screen.getByText('+50')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Ocultar motivos do pedido 1001' })).toHaveAttribute('aria-expanded', 'true')
  })

  it('pedido sem fatores diz isso em vez de ficar em branco', async () => {
    renderWithApp(<OrdersPage />, clientWith(page([evaluation({ factors: [] })])))

    await userEvent.click(await screen.findByRole('button', { name: /Ver motivos/ }))

    expect(screen.getByText('Nenhum fator de risco registrado para este pedido.')).toBeInTheDocument()
  })

  it('sem pedidos, explica o que fazer', async () => {
    renderWithApp(<OrdersPage />, clientWith(page([])))

    expect(await screen.findByText('Ainda não há pedidos avaliados.')).toBeInTheDocument()
    expect(screen.getByText(/Confira se a loja está conectada/)).toBeInTheDocument()
  })

  it('filtro sem resultado orienta a limpar o filtro', async () => {
    renderWithApp(<OrdersPage />, clientWith(page([])))
    await screen.findByText('Ainda não há pedidos avaliados.')

    await userEvent.selectOptions(screen.getByLabelText('Nível de risco'), 'Medium')

    expect(await screen.findByText('Nenhum pedido com risco médio.')).toBeInTheDocument()
  })

  it('erro mostra mensagem em português e permite tentar de novo', async () => {
    const get = vi
      .fn()
      .mockImplementationOnce(async () => tenant)
      .mockRejectedValue(new ApiError(503, 'x', 'interno'))
    renderWithApp(<OrdersPage />, fakeClient({ get }))

    expect(await screen.findByRole('alert')).toHaveTextContent('O RiskGuard está indisponível no momento.')
    expect(screen.getByRole('button', { name: 'Tentar de novo' })).toBeInTheDocument()
  })

  it('pagina: Próxima pede a página 2', async () => {
    const client = clientWith(page([evaluation()], 45))
    renderWithApp(<OrdersPage />, client)

    await userEvent.click(await screen.findByRole('button', { name: /Próxima/ }))

    await waitFor(() => expect(client.get).toHaveBeenCalledWith('/api/v1/risk/evaluations', { page: 2, pageSize: 20, level: undefined }))
  })
})
