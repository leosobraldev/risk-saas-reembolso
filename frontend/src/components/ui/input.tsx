import type { ComponentProps } from 'react'
import { cn } from '@/lib/utils'

const field =
  'h-9 w-full rounded-md border border-rule-strong bg-surface px-3 text-sm text-ink placeholder:text-ink-muted disabled:cursor-not-allowed disabled:opacity-50 aria-[invalid=true]:border-2 aria-[invalid=true]:border-ink'

export function Input({ className, ...props }: ComponentProps<'input'>) {
  return <input className={cn(field, className)} {...props} />
}

/** Select nativo estilizado: teclado, leitor de tela e mobile funcionam sem custo. */
export function Select({ className, ...props }: ComponentProps<'select'>) {
  return <select className={cn(field, 'pr-8', className)} {...props} />
}

export function Label({ className, ...props }: ComponentProps<'label'>) {
  return <label className={cn('text-sm font-semibold text-ink', className)} {...props} />
}
