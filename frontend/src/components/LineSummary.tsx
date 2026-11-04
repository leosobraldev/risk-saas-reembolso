import { useActionCount, useEvaluationCount } from '@/api/queries'
import type { RiskLevel } from '@/api/types'
import { LEVEL_LABEL } from '@/lib/labels'
import { cn } from '@/lib/utils'

const STATIONS: { level: RiskLevel; line: string; dot: string }[] = [
  { level: 'Low', line: 'Linha Verde', dot: 'bg-line-green' },
  { level: 'Medium', line: 'Linha Amarela', dot: 'bg-line-yellow' },
  { level: 'High', line: 'Linha Vermelha', dot: 'bg-line-red' },
]

function Station({ level, line, dot, selected, onSelect }: { level: RiskLevel; line: string; dot: string; selected: boolean; onSelect: () => void }) {
  const count = useEvaluationCount(level)
  return (
    <button
      type="button"
      onClick={onSelect}
      aria-pressed={selected}
      title={line}
      className={cn(
        'relative z-10 flex min-w-[5.5rem] flex-col items-center gap-1 rounded-md px-2 py-2 text-center sm:min-w-28 sm:px-3 hover:bg-sunken',
        selected && 'bg-sunken ring-1 ring-ink',
      )}
    >
      <span aria-hidden="true" className={cn('size-4 rounded-full border-2 border-ink', dot)} />
      <span className="font-label text-xs font-semibold uppercase tracking-wide text-ink-muted">Risco {LEVEL_LABEL[level]}</span>
      <span className="text-lg font-bold leading-none text-ink">{count.isPending ? '–' : count.isError ? '?' : count.data}</span>
    </button>
  )
}

/**
 * Resumo como um trajeto de metrô: três estações (Verde, Amarela, Vermelha) com o total de avaliações de cada nível,
 * tudo lido da API. Clicar numa estação filtra a tabela; clicar de novo limpa.
 */
export function LineSummary({ selected, onSelect }: { selected?: RiskLevel; onSelect: (level?: RiskLevel) => void }) {
  const failed = useActionCount('Failed')

  return (
    <section aria-label="Resumo por nível de risco" className="flex flex-wrap items-center justify-between gap-4 rounded-lg border border-rule bg-surface px-4 py-3">
      <div className="relative flex items-center gap-2">
        <div aria-hidden="true" className="absolute inset-x-8 top-[1.15rem] z-0 flex h-1">
          <span className="flex-1 bg-line-green" />
          <span className="flex-1 bg-line-yellow" />
          <span className="flex-1 bg-line-red" />
        </div>
        {STATIONS.map((station) => (
          <Station key={station.level} {...station} selected={selected === station.level} onSelect={() => onSelect(selected === station.level ? undefined : station.level)} />
        ))}
      </div>

      <div className="text-sm">
        <p className="font-label text-xs font-semibold uppercase tracking-wide text-ink-muted">Ações com falha</p>
        <p className="font-semibold text-ink">
          {failed.isPending ? '–' : failed.isError ? 'indisponível' : failed.data === 0 ? 'Nenhuma' : `${failed.data} para revisar`}
        </p>
      </div>
    </section>
  )
}
