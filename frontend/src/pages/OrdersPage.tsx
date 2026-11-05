import { ChevronDown, ChevronRight } from 'lucide-react'
import { Link } from 'react-router'
import { Fragment, useState } from 'react'
import { PAGE_SIZE, useEvaluations, useTenant } from '@/api/queries'
import type { RiskEvaluation, RiskLevel } from '@/api/types'
import { RiskBadge } from '@/components/RiskBadge'
import { LineSummary } from '@/components/LineSummary'
import { RiskTrack } from '@/components/RiskTrack'
import { PageHeader } from '@/components/PageHeader'
import { Pager } from '@/components/Pager'
import { EmptyState, ErrorState, TableSkeleton } from '@/components/states'
import { Select } from '@/components/ui/input'
import { TBody, THead, Table, Td, Th, Tr } from '@/components/ui/table'
import { formatBRL, formatDateTime, formatRelative, platformLabel } from '@/lib/format'
import { LEVEL_LABEL } from '@/lib/labels'

const LEVELS: RiskLevel[] = ['High', 'Medium', 'Low']

export function OrdersPage() {
  const [page, setPage] = useState(1)
  const [level, setLevel] = useState<RiskLevel | undefined>()
  const evaluations = useEvaluations(page, level)
  const tenant = useTenant()
  const threshold = tenant.data?.settings.highRiskThreshold

  const changeLevel = (next?: RiskLevel) => {
    setLevel(next)
    setPage(1)
  }

  return (
    <>
      <PageHeader title="Pedidos avaliados" description="Cada pedido recebe um score de risco de reembolso assim que a loja o envia. Os mais recentes aparecem primeiro." />

      <div className="flex flex-col gap-4">
        <LineSummary selected={level} onSelect={changeLevel} />

        <div className="flex flex-wrap items-center gap-3">
          <label htmlFor="level-filter" className="text-sm font-semibold">
            Nível de risco
          </label>
          <Select id="level-filter" className="w-48" value={level ?? ''} onChange={(event) => changeLevel((event.target.value || undefined) as RiskLevel | undefined)}>
            <option value="">Todos os níveis</option>
            {LEVELS.map((value) => (
              <option key={value} value={value}>
                {LEVEL_LABEL[value]}
              </option>
            ))}
          </Select>
        </div>

        {evaluations.isPending ? (
          <TableSkeleton />
        ) : evaluations.isError ? (
          <ErrorState error={evaluations.error} onRetry={() => void evaluations.refetch()} />
        ) : evaluations.data.items.length === 0 ? (
          <EmptyState title={level ? `Nenhum pedido com risco ${LEVEL_LABEL[level].toLowerCase()}.` : 'Ainda não há pedidos avaliados.'}>
            {level
              ? 'Limpe o filtro para ver todos os pedidos.'
              : 'Quando a sua loja enviar o primeiro pedido, ele aparece aqui com o score. Confira se a loja está conectada em Configurações.'}
          </EmptyState>
        ) : (
          <>
            <Table aria-label="Pedidos avaliados">
              <THead>
                <Tr>
                  <Th className="w-10">
                    <span className="sr-only">Detalhes</span>
                  </Th>
                  <Th>Pedido</Th>
                  <Th className="hidden text-right sm:table-cell">Valor</Th>
                  <Th>Risco</Th>
                  <Th>Score{threshold !== undefined && <span className="ml-1.5 hidden font-normal normal-case tracking-normal sm:inline">· limite {threshold}</span>}</Th>
                  <Th className="hidden md:table-cell">Avaliado</Th>
                </Tr>
              </THead>
              <TBody>
                {evaluations.data.items.map((row) => (
                  <OrderRow key={row.id} row={row} threshold={threshold} />
                ))}
              </TBody>
            </Table>
            <Pager page={evaluations.data.page} pageSize={PAGE_SIZE} total={evaluations.data.total} onPage={setPage} />
          </>
        )}
      </div>
    </>
  )
}

function OrderRow({ row, threshold }: { row: RiskEvaluation; threshold?: number }) {
  const [open, setOpen] = useState(false)
  const detailsId = `factors-${row.id}`

  return (
    <Fragment>
      <Tr>
        <Td>
          <button
            type="button"
            onClick={() => setOpen((value) => !value)}
            aria-expanded={open}
            aria-controls={detailsId}
            aria-label={`${open ? 'Ocultar' : 'Ver'} motivos do pedido ${row.externalOrderId}`}
            className="grid size-7 place-items-center rounded-md hover:bg-sunken"
          >
            {open ? <ChevronDown aria-hidden="true" className="size-4" /> : <ChevronRight aria-hidden="true" className="size-4" />}
          </button>
        </Td>
        <Td>
          <span className="font-semibold">#{row.externalOrderId}</span>
          <span className="block text-xs text-ink-muted sm:ml-2 sm:inline sm:text-sm">{platformLabel(row.platform)}</span>
        </Td>
        <Td className="hidden text-right sm:table-cell">{formatBRL(row.totalAmount)}</Td>
        <Td>
          <RiskBadge level={row.level} />
        </Td>
        <Td>
          <div className="flex flex-col-reverse items-start gap-1 sm:flex-row sm:items-center sm:gap-3">
            <RiskTrack score={row.score} level={row.level} threshold={threshold} />
            <span className="w-8 font-semibold">{row.score}</span>
          </div>
        </Td>
        <Td title={formatDateTime(row.calculatedAt)} className="hidden whitespace-nowrap text-ink-muted md:table-cell">
          {formatRelative(row.calculatedAt)}
        </Td>
      </Tr>
      {open && (
        <Tr id={detailsId} className="bg-sunken">
          <Td />
          <Td colSpan={5} className="whitespace-normal">
            {row.factors.length === 0 ? (
              <p className="text-ink-muted">Nenhum fator de risco registrado para este pedido.</p>
            ) : (
              <>
                <p className="mb-1 font-label text-xs font-semibold uppercase tracking-wide text-ink-muted">Por que este score</p>
                <ul className="flex flex-col gap-1">
                  {row.factors.map((factor) => (
                    <li key={factor.reason} className="flex max-w-xl items-baseline justify-between gap-4">
                      <span>{factor.reason}</span>
                      <span className="font-semibold">+{factor.pointsAdded}</span>
                    </li>
                  ))}
                </ul>
              </>
            )}
            {threshold !== undefined && row.score > threshold && (
              <p className="mt-3 text-ink-muted">
                Score acima do limite: se o RiskGuard reteve este pedido, libere-o em{' '}
                <Link to="/acoes" className="font-semibold text-ink underline underline-offset-2">
                  Ações
                </Link>
                .
              </p>
            )}
          </Td>
        </Tr>
      )}
    </Fragment>
  )
}
