import type { ReactNode } from 'react'

export function PageHeader({ title, description, actions }: { title: string; description?: string; actions?: ReactNode }) {
  return (
    <header className="mb-6 flex flex-wrap items-end justify-between gap-3">
      <div>
        {/* placa de estação: anel + trecho de linha, sempre na cor da marca (as cores de linha só dizem risco) */}
        <span aria-hidden="true" className="mb-2 flex items-center">
          <span className="size-2.5 rounded-full border-2 border-rail bg-surface" />
          <span className="h-0.5 w-10 bg-rail" />
        </span>
        <h1 className="text-2xl font-bold tracking-tight text-ink">{title}</h1>
        {description && <p className="mt-1 max-w-prose text-sm text-ink-muted">{description}</p>}
      </div>
      {actions}
    </header>
  )
}
