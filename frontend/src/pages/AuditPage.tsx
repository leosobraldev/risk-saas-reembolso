import { useState } from 'react'
import { PAGE_SIZE, useAuditEvents } from '@/api/queries'
import { PageHeader } from '@/components/PageHeader'
import { Pager } from '@/components/Pager'
import { EmptyState, ErrorState, TableSkeleton } from '@/components/states'
import { Select } from '@/components/ui/input'
import { TBody, THead, Table, Td, Th, Tr } from '@/components/ui/table'
import { formatDateTime, formatRelative } from '@/lib/format'
import { actionLabel, actorLabel, AUDIT_ACTIONS } from '@/lib/labels'

export function AuditPage() {
  const [page, setPage] = useState(1)
  const [action, setAction] = useState<string | undefined>()
  const events = useAuditEvents(page, action)

  return (
    <>
      <PageHeader title="Auditoria" description="Quem mudou o quê na sua conta. O registro não pode ser alterado nem apagado por ninguém da sua loja." />

      <div className="flex flex-col gap-4">
        <div className="flex flex-wrap items-center gap-3">
          <label htmlFor="action-filter" className="text-sm font-semibold">
            Tipo de evento
          </label>
          <Select
            id="action-filter"
            className="w-72"
            value={action ?? ''}
            onChange={(event) => {
              setAction(event.target.value || undefined)
              setPage(1)
            }}
          >
            <option value="">Todos os eventos</option>
            {Object.entries(AUDIT_ACTIONS).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </Select>
        </div>

        {events.isPending ? (
          <TableSkeleton />
        ) : events.isError ? (
          <ErrorState error={events.error} onRetry={() => void events.refetch()} />
        ) : events.data.items.length === 0 ? (
          <EmptyState title="Nenhum evento encontrado.">
            {action ? 'Limpe o filtro para ver todos os eventos.' : 'Alterações de regras, conexões e liberações aparecem aqui.'}
          </EmptyState>
        ) : (
          <>
            <Table aria-label="Eventos de auditoria">
              <THead>
                <Tr>
                  <Th>Quando</Th>
                  <Th>Evento</Th>
                  <Th>Detalhes</Th>
                  <Th>Quem</Th>
                </Tr>
              </THead>
              <TBody>
                {events.data.items.map((event) => (
                  <Tr key={event.id}>
                    <Td title={formatDateTime(event.occurredAt)} className="whitespace-nowrap text-ink-muted">
                      {formatRelative(event.occurredAt)}
                    </Td>
                    <Td className="font-semibold">{actionLabel(event.action)}</Td>
                    <Td className="max-w-md">{event.details ?? <span className="text-ink-muted">—</span>}</Td>
                    <Td className="whitespace-nowrap">{actorLabel(event.actorId)}</Td>
                  </Tr>
                ))}
              </TBody>
            </Table>
            <Pager page={events.data.page} pageSize={PAGE_SIZE} total={events.data.total} onPage={setPage} />
          </>
        )}
      </div>
    </>
  )
}
