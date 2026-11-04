import type { OrderActionStatus, RiskLevel } from '@/api/types'
import { LEVEL_LABEL, STATUS_LABEL } from '@/lib/labels'
import { cn } from '@/lib/utils'

/**
 * Linhas do Metrô: Verde = baixo, Amarela = médio, Vermelha = alto. A cor nunca é o único sinal:
 * o texto do nível e o símbolo (formas diferentes) acompanham sempre.
 */
const LEVELS: Record<RiskLevel, { line: string; className: string; shape: string }> = {
  Low: { line: 'Verde', className: 'bg-line-green text-white', shape: 'M3 8h10' },
  Medium: { line: 'Amarela', className: 'bg-line-yellow text-ink', shape: 'M3 8h10M8 3v10' },
  High: { line: 'Vermelha', className: 'bg-line-red text-white', shape: 'M3 3l10 10M13 3L3 13' },
}

export function RiskBadge({ level, className }: { level: RiskLevel; className?: string }) {
  const info = LEVELS[level]
  return (
    <span
      title={`Linha ${info.line}`}
      className={cn('inline-flex h-6 items-center gap-1.5 whitespace-nowrap rounded-sm px-2 font-label text-xs font-semibold uppercase tracking-wide', info.className, className)}
    >
      <svg aria-hidden="true" viewBox="0 0 16 16" className="size-3" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
        <path d={info.shape} />
      </svg>
      Risco {LEVEL_LABEL[level]}
    </span>
  )
}

/** Situação da ação: nunca usa as cores de linha (reservadas ao risco). Texto + símbolo + peso do contorno distinguem. */
const STATUS: Record<OrderActionStatus, { className: string; shape: string }> = {
  Pending: { className: 'border-rule-strong bg-sunken text-ink', shape: 'M8 4v4l3 2' },
  Succeeded: { className: 'border-rail bg-rail text-white', shape: 'M3 8.5l3.5 3.5L13 5' },
  Failed: { className: 'border-ink bg-surface text-ink border-2', shape: 'M8 3v6M8 12.5v.5' },
  Skipped: { className: 'border-rule-strong bg-sunken text-ink-muted', shape: 'M4 8h8' },
  Releasing: { className: 'border-rail bg-[#e3ebf4] text-rail', shape: 'M13 8a5 5 0 1 1-1.5-3.5M13 3v3h-3' },
  Released: { className: 'border-rail bg-surface text-rail', shape: 'M3 8.5l3.5 3.5L13 5' },
  ReleaseFailed: { className: 'border-ink bg-surface text-ink border-2', shape: 'M8 3v6M8 12.5v.5' },
}

export function StatusBadge({ status }: { status: OrderActionStatus }) {
  const info = STATUS[status]
  return (
    <span className={cn('inline-flex h-6 items-center gap-1.5 whitespace-nowrap rounded-sm border px-2 font-label text-xs font-semibold uppercase tracking-wide', info.className)}>
      <svg aria-hidden="true" viewBox="0 0 16 16" className="size-3" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d={info.shape} />
      </svg>
      {STATUS_LABEL[status]}
    </span>
  )
}
