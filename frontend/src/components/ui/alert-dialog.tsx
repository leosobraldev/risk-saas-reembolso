import { AlertDialog as Primitive } from 'radix-ui'
import type { ComponentProps } from 'react'
import { cn } from '@/lib/utils'
import { buttonVariants } from './button-variants'

export const AlertDialog = Primitive.Root
export const AlertDialogTrigger = Primitive.Trigger

export function AlertDialogContent({ className, children, ...props }: ComponentProps<typeof Primitive.Content>) {
  return (
    <Primitive.Portal>
      <Primitive.Overlay className="fixed inset-0 z-40 bg-rail/60 data-[state=open]:animate-in data-[state=open]:fade-in-0" />
      <Primitive.Content
        className={cn(
          'fixed left-1/2 top-1/2 z-50 w-[calc(100%-2rem)] max-w-md -translate-x-1/2 -translate-y-1/2 rounded-lg border border-rule-strong bg-surface p-6 shadow-lg data-[state=open]:animate-in data-[state=open]:fade-in-0',
          className,
        )}
        {...props}
      >
        {children}
      </Primitive.Content>
    </Primitive.Portal>
  )
}

export function AlertDialogTitle({ className, ...props }: ComponentProps<typeof Primitive.Title>) {
  return <Primitive.Title className={cn('text-lg font-bold text-ink', className)} {...props} />
}

export function AlertDialogDescription({ className, ...props }: ComponentProps<typeof Primitive.Description>) {
  return <Primitive.Description className={cn('mt-2 text-sm text-ink-muted', className)} {...props} />
}

export function AlertDialogFooter({ className, ...props }: ComponentProps<'div'>) {
  return <div className={cn('mt-6 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end', className)} {...props} />
}

export function AlertDialogCancel({ className, ...props }: ComponentProps<typeof Primitive.Cancel>) {
  return <Primitive.Cancel className={cn(buttonVariants({ variant: 'outline' }), className)} {...props} />
}

export function AlertDialogAction({ className, ...props }: ComponentProps<typeof Primitive.Action>) {
  return <Primitive.Action className={cn(buttonVariants({ variant: 'primary' }), className)} {...props} />
}
