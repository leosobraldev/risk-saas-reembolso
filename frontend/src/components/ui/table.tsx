import type { ComponentProps } from 'react'
import { cn } from '@/lib/utils'

export function Table({ className, ...props }: ComponentProps<'table'>) {
  return (
    <div className="overflow-x-auto rounded-lg border border-rule bg-surface">
      <table className={cn('w-full border-collapse text-sm', className)} {...props} />
    </div>
  )
}

export function THead(props: ComponentProps<'thead'>) {
  return <thead className="border-b border-rule-strong bg-sunken" {...props} />
}

export function TBody(props: ComponentProps<'tbody'>) {
  return <tbody className="[&>tr:last-child]:border-b-0" {...props} />
}

export function Tr({ className, ...props }: ComponentProps<'tr'>) {
  return <tr className={cn('border-b border-rule align-middle', className)} {...props} />
}

export function Th({ className, ...props }: ComponentProps<'th'>) {
  return (
    <th
      scope="col"
      className={cn('h-10 px-3 text-left font-label text-xs font-semibold uppercase tracking-wide text-ink-muted', className)}
      {...props}
    />
  )
}

export function Td({ className, ...props }: ComponentProps<'td'>) {
  return <td className={cn('px-3 py-2.5', className)} {...props} />
}
