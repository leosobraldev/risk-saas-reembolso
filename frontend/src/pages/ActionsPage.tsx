import { useState } from 'react'
import { errorMessage } from '@/api/errors'
import { PAGE_SIZE, useActions, useReleaseAction } from '@/api/queries'
import type { OrderAction, OrderActionStatus } from '@/api/types'
import { StatusBadge } from '@/components/RiskBadge'
import { PageHeader } from '@/components/PageHeader'
import { Pager } from '@/components/Pager'
import { EmptyState, ErrorState, TableSkeleton } from '@/components/states'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogTitle,
  AlertDialogTrigger,
} from '@/components/ui/alert-dialog'
import { Button } from '@/components/ui/button'
import { Select } from '@/components/ui/input'
import { TBody, THead, Table, Td, Th, Tr } from '@/components/ui/table'
import { formatDateTime, formatRelative, platformLabel } from '@/lib/format'
import { ACTION_TYPE_LABEL, STATUS_LABEL } from '@/lib/labels'

const STATUSES: OrderActionStatus[] = ['Succeeded', 'Failed', 'Pending', 'Releasing', 'Released', 'ReleaseFailed', 'Skipped']

export function ActionsPage() {
  const [page, setPage] = useState(1)
  const [status, setStatus] = useState<OrderActionStatus | undefined>()
  const actions = useActions(page, status)
  const release = useReleaseAction()

  return (
    <>
      <PageHeader
        title="Ações do RiskGuard"
        description="O que o RiskGuard fez nos pedidos de risco alto. Se um pedido foi retido por engano, libere-o aqui."
      />

      <div className="flex flex-col gap-4">
        <div className="flex flex-wrap items-center gap-3">
          <label htmlFor="status-filter" className="text-sm font-semibold">
            Situação
          </label>
          <Select
            id="status-filter"
            className="w-52"
            value={status ?? ''}
            onChange={(event) => {
              setStatus((event.target.value || undefined) as OrderActionStatus | undefined)
              setPage(1)
            }}
          >
            <option value="">Todas as situações</option>
            {STATUSES.map((value) => (
              <option key={value} value={value}>
                {STATUS_LABEL[value]}
              </option>
            ))}
          </Select>
        </div>

        {release.isError && (
          <p role="alert" className="rounded-md border-2 border-ink bg-surface px-3 py-2 text-sm font-semibold">
            {errorMessage(release.error)}
          </p>
        )}

        {actions.isPending ? (
          <TableSkeleton />
        ) : actions.isError ? (
          <ErrorState error={actions.error} onRetry={() => void actions.refetch()} />
        ) : actions.data.items.length === 0 ? (
          <EmptyState title={status ? 'Nenhuma ação com esta situação.' : 'O RiskGuard ainda não agiu em nenhum pedido.'}>
            {status
              ? 'Limpe o filtro para ver todas as ações.'
              : 'Ações aparecem quando um pedido passa do limite de risco alto. Ajuste o limite e o bloqueio automático em Configurações.'}
          </EmptyState>
        ) : (
          <>
            <Table aria-label="Ações do RiskGuard">
              <THead>
                <Tr>
                  <Th>Pedido</Th>
                  <Th>Ação</Th>
                  <Th>Situação</Th>
                  <Th className="text-right">Score</Th>
                  <Th>Quando</Th>
                  <Th className="text-right">
                    <span className="sr-only">Liberar</span>
                  </Th>
                </Tr>
              </THead>
              <TBody>
                {actions.data.items.map((action) => (
                  <ActionRow key={action.id} action={action} onRelease={() => release.mutate(action.id)} releasing={release.isPending && release.variables === action.id} />
                ))}
              </TBody>
            </Table>
            <Pager page={actions.data.page} pageSize={PAGE_SIZE} total={actions.data.total} onPage={setPage} />
          </>
        )}
      </div>
    </>
  )
}

function ActionRow({ action, onRelease, releasing }: { action: OrderAction; onRelease: () => void; releasing: boolean }) {
  const canRelease = action.status === 'Succeeded'

  return (
    <Tr key={`${action.id}-${action.status}`} className={action.status === 'Releasing' || action.status === 'Released' ? 'station-change' : undefined}>
      <Td>
        <span className="font-semibold">#{action.externalOrderId}</span>
        <span className="block text-xs text-ink-muted sm:ml-2 sm:inline sm:text-sm">{platformLabel(action.platform)}</span>
        {action.lastError && (action.status === 'Failed' || action.status === 'ReleaseFailed' || action.status === 'Skipped') && (
          <p className="mt-0.5 max-w-md text-xs text-ink-muted">{action.lastError}</p>
        )}
      </Td>
      <Td>{ACTION_TYPE_LABEL[action.type]}</Td>
      <Td>
        <StatusBadge status={action.status} />
      </Td>
      <Td className="text-right font-semibold">{action.score}</Td>
      <Td title={formatDateTime(action.createdAt)} className="whitespace-nowrap text-ink-muted">
        {formatRelative(action.createdAt)}
      </Td>
      <Td className="text-right">
        {canRelease && (
          <AlertDialog>
            <AlertDialogTrigger asChild>
              <Button variant="outline" size="sm" disabled={releasing}>
                Liberar pedido
              </Button>
            </AlertDialogTrigger>
            <AlertDialogContent>
              <AlertDialogTitle>Liberar o pedido #{action.externalOrderId}?</AlertDialogTitle>
              <AlertDialogDescription>
                O RiskGuard desfaz a retenção na {platformLabel(action.platform)}, e o pedido segue o fluxo normal da loja. A liberação fica registrada na auditoria.
              </AlertDialogDescription>
              <AlertDialogFooter>
                <AlertDialogCancel>Manter retido</AlertDialogCancel>
                <AlertDialogAction onClick={onRelease}>Liberar pedido</AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        )}
      </Td>
    </Tr>
  )
}
