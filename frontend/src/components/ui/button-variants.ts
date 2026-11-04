import { cva } from 'class-variance-authority'

export const buttonVariants = cva(
  'inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-md border text-sm font-semibold transition-colors disabled:pointer-events-none disabled:opacity-50 [&_svg]:size-4 [&_svg]:shrink-0',
  {
    variants: {
      variant: {
        primary: 'border-rail bg-rail text-white hover:bg-[#12406d]',
        outline: 'border-rule-strong bg-surface text-ink hover:bg-sunken',
        danger: 'border-ink bg-ink text-white hover:bg-black',
        ghost: 'border-transparent bg-transparent text-ink hover:bg-sunken',
      },
      size: {
        default: 'h-9 px-4',
        sm: 'h-8 px-3',
        icon: 'size-9',
      },
    },
    defaultVariants: { variant: 'primary', size: 'default' },
  },
)
