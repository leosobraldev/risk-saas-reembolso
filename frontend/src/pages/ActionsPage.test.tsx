import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ApiError } from '@/api/client'
import { action, page } from '@/test/fixtures'
import { fakeClient, renderWithApp } from '@/test/render'
import { ActionsPage } from './ActionsPage'

const clientWith = (items: ReturnType<typeof action>[], post = vi.fn().mockResolvedValue(action({ status: 'Releasing' }))) =>
  fakeClient({ get: vi.fn().mockResolvedValue(page(items)), post })

describe('ActionsPage', () => {
  it('mostra o que o RiskGuard fez, com a situação em texto', async () => {
    renderWithApp(
      <ActionsPage />,
      clientWith([
        action(),
        action({ id: 'ac-2', externalOrderId: '1002', type: 'Flag', status: 'Failed', lastError: 'A loja recusou a requisição.' }),
      ]),
    )

    const table = await screen.findByRole('table', { name: 'Ações do RiskGuard' })
    expect(within(table).getByText('Bloqueio')).toBeInTheDocument()
    expect(within(table).getByText('Sinalização')).toBeInTheDocument()
    expect(within(table).getByText('Aplicada')).toBeInTheDocument()
    expect(within(table).getByText('Falhou')).toBeInTheDocument()
    expect(within(table).getByText('A loja recusou a requisição.')).toBeInTheDocument()
  })

  it('só ações aplicadas com sucesso têm o botão de liberar', async () => {
    renderWithApp(
      <ActionsPage />,
      clientWith([
        action({ id: 'a', externalOrderId: '1', status: 'Succeeded' }),
        action({ id: 'b', externalOrderId: '2', status: 'Failed' }),
        action({ id: 'c', externalOrderId: '3', status: 'Released' }),
        action({ id: 'd', externalOrderId: '4', status: 'Releasing' }),
        action({ id: 'e', externalOrderId: '5', status: 'Skipped' }),
      ]),
    )

    await screen.findByRole('table')

    expect(screen.getAllByRole('button', { name: 'Liberar pedido' })).toHaveLength(1)
  })

  it('liberar pede confirmação e só então chama a API', async () => {
    const post = vi.fn().mockResolvedValue(action({ status: 'Releasing' }))
    renderWithApp(<ActionsPage />, clientWith([action()], post))

    await userEvent.click(await screen.findByRole('button', { name: 'Liberar pedido' }))
    const dialog = await screen.findByRole('alertdialog')
    expect(within(dialog).getByText('Liberar o pedido #1001?')).toBeInTheDocument()
    expect(post).not.toHaveBeenCalled()

    await userEvent.click(within(dialog).getByRole('button', { name: 'Liberar pedido' }))

    await waitFor(() => expect(post).toHaveBeenCalledWith('/api/v1/actions/ac-1/release'))
  })

  it('cancelar a confirmação não libera nada', async () => {
    const post = vi.fn()
    renderWithApp(<ActionsPage />, clientWith([action()], post))

    await userEvent.click(await screen.findByRole('button', { name: 'Liberar pedido' }))
    await userEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Manter retido' }))

    expect(post).not.toHaveBeenCalled()
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })

  it('se a API recusar a liberação, explica o motivo em português', async () => {
    const post = vi.fn().mockRejectedValue(new ApiError(409, 'action.not_releasable', 'x'))
    renderWithApp(<ActionsPage />, clientWith([action()], post))

    await userEvent.click(await screen.findByRole('button', { name: 'Liberar pedido' }))
    await userEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Liberar pedido' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Esta ação não pode mais ser liberada')
  })

  it('filtrar por situação pede só aquela situação à API', async () => {
    const client = clientWith([action()])
    renderWithApp(<ActionsPage />, client)
    await screen.findByRole('table')

    await userEvent.selectOptions(screen.getByLabelText('Situação'), 'Failed')

    await waitFor(() => expect(client.get).toHaveBeenCalledWith('/api/v1/actions', { page: 1, pageSize: 20, status: 'Failed' }))
  })

  it('sem ações, explica quando elas aparecem', async () => {
    renderWithApp(<ActionsPage />, clientWith([]))

    expect(await screen.findByText('O RiskGuard ainda não agiu em nenhum pedido.')).toBeInTheDocument()
  })

  it('enquanto há ação sendo liberada, a lista é atualizada sozinha até concluir', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    try {
      const get = vi
        .fn()
        .mockResolvedValueOnce(page([action({ status: 'Releasing' })]))
        .mockResolvedValue(page([action({ status: 'Released' })]))
      renderWithApp(<ActionsPage />, fakeClient({ get }))

      const table = await screen.findByRole('table', { name: 'Ações do RiskGuard' })
      expect(within(table).getByText('Liberando')).toBeInTheDocument()
      await vi.advanceTimersByTimeAsync(3_100)

      expect(await within(table).findByText('Liberada')).toBeInTheDocument()
      expect(within(table).queryByText('Liberando')).not.toBeInTheDocument()
    } finally {
      vi.useRealTimers()
    }
  })
})
