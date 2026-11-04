import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { auditEvent, page } from '@/test/fixtures'
import { fakeClient, renderWithApp } from '@/test/render'
import { AuditPage } from './AuditPage'

describe('AuditPage', () => {
  it('mostra o evento em português, os detalhes e quem fez (sem expor o id do provedor)', async () => {
    renderWithApp(
      <AuditPage />,
      fakeClient({
        get: vi.fn().mockResolvedValue(
          page([
            auditEvent(),
            auditEvent({ id: 'au-2', action: 'tenant.registered', actorId: 'system', details: null }),
            auditEvent({ id: 'au-3', action: 'evento.novo', details: 'x' }),
          ]),
        ),
      }),
    )

    const table = await screen.findByRole('table', { name: 'Eventos de auditoria' })
    expect(within(table).getByText('Regras de risco alteradas')).toBeInTheDocument()
    expect(within(table).getByText(/limite 80 -> 65/)).toBeInTheDocument()
    expect(within(table).getAllByText('Usuário da loja')).toHaveLength(2) // os dois eventos de usuário; o `system` vira RiskGuard
    expect(within(table).getByText('RiskGuard')).toBeInTheDocument()
    expect(within(table).getByText('Loja cadastrada')).toBeInTheDocument()
    expect(within(table).getByText('evento.novo')).toBeInTheDocument() // ação futura aparece com o código, nunca some
    expect(within(table).queryByText(/auth0\|abc/)).not.toBeInTheDocument()
  })

  it('filtra pelo tipo de evento', async () => {
    const get = vi.fn().mockResolvedValue(page([auditEvent()]))
    renderWithApp(<AuditPage />, fakeClient({ get }))
    await screen.findByRole('table')

    await userEvent.selectOptions(screen.getByLabelText('Tipo de evento'), 'connection.removed')

    await waitFor(() => expect(get).toHaveBeenCalledWith('/api/v1/audit-events', { page: 1, pageSize: 20, action: 'connection.removed' }))
  })

  it('sem eventos, explica o que aparece aqui', async () => {
    renderWithApp(<AuditPage />, fakeClient({ get: vi.fn().mockResolvedValue(page([])) }))

    expect(await screen.findByText('Nenhum evento encontrado.')).toBeInTheDocument()
    expect(screen.getByText(/Alterações de regras, conexões e liberações/)).toBeInTheDocument()
  })
})
