import { cva, type VariantProps } from 'class-variance-authority'
import type { ComponentProps } from 'react'
import { Slot } from 'radix-ui'
import { cn } from '@/lib/utils'

const buttonVariants = cva(
  'inline-flex shrink-0 cursor-pointer items-center justify-center gap-1.5 rounded-md text-sm font-medium whitespace-nowrap transition-[background-color,color,box-shadow,transform] duration-150 ease-out outline-none active:translate-y-px disabled:pointer-events-none disabled:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0',
  {
    variants: {
      variant: {
        primary: 'bg-ink text-surface hover:bg-ink/90',
        accent: 'bg-accent text-accent-ink hover:brightness-105',
        secondary: 'bg-surface text-ink hairline hover:bg-surface-sunken',
        ghost: 'text-ink-secondary hover:bg-surface-sunken hover:text-ink',
        danger: 'bg-negative-soft text-negative hover:brightness-95',
        link: 'text-accent underline-offset-4 hover:underline',
      },
      size: {
        sm: 'h-7 px-2.5 text-xs',
        md: 'h-8 px-3',
        lg: 'h-10 px-4 text-[0.9375rem]',
        icon: 'size-8',
      },
    },
    defaultVariants: { variant: 'secondary', size: 'md' },
  },
)

export function Button({
  className,
  variant,
  size,
  asChild = false,
  ...props
}: ComponentProps<'button'> & VariantProps<typeof buttonVariants> & { asChild?: boolean }) {
  const Comp = asChild ? Slot.Root : 'button'
  return <Comp data-slot="button" className={cn(buttonVariants({ variant, size, className }))} {...props} />
}

export { buttonVariants }
