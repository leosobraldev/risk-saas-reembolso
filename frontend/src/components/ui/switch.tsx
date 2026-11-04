import { Switch as SwitchPrimitive } from 'radix-ui'
import type { ComponentProps } from 'react'
import { cn } from '@/lib/utils'

export function Switch({ className, ...props }: ComponentProps<typeof SwitchPrimitive.Root>) {
  return (
    <SwitchPrimitive.Root
      className={cn(
        'inline-flex h-6 w-11 shrink-0 items-center rounded-full border border-rule-strong bg-sunken p-0.5 transition-colors data-[state=checked]:border-rail data-[state=checked]:bg-rail disabled:opacity-50',
        className,
      )}
      {...props}
    >
      <SwitchPrimitive.Thumb className="block size-4.5 rounded-full bg-white shadow-sm ring-1 ring-rule-strong transition-transform data-[state=checked]:translate-x-5" />
    </SwitchPrimitive.Root>
  )
}
