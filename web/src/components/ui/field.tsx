import type { ComponentProps, ReactNode } from 'react'
import { cn } from '@/lib/utils'

export function Field({
  label,
  error,
  children,
  hint,
}: {
  label: string
  error?: string
  hint?: string
  children: ReactNode
}) {
  return (
    <label className="block space-y-1.5 text-sm">
      <span className="font-medium text-ink">{label}</span>
      {children}
      {hint && !error ? <span className="block text-xs text-ink-tertiary">{hint}</span> : null}
      {error ? (
        <span role="alert" className="block text-xs text-negative">
          {error}
        </span>
      ) : null}
    </label>
  )
}

export function Fieldset({ legend, children, className }: { legend: string; children: ReactNode; className?: string }) {
  return (
    <fieldset className={cn('space-y-4 border-t border-line pt-5', className)}>
      <legend className="pr-3 text-2xs font-medium tracking-[0.08em] text-ink-tertiary uppercase">{legend}</legend>
      {children}
    </fieldset>
  )
}

const control =
  'w-full rounded-md border border-line bg-surface px-3 py-2 text-sm text-ink placeholder:text-ink-tertiary transition-[border-color,box-shadow] duration-150 hover:border-line-strong focus:border-accent focus:outline-none focus:ring-2 focus:ring-focus disabled:opacity-50 aria-invalid:border-negative'

export function Input({ className, ...props }: ComponentProps<'input'>) {
  return <input className={cn(control, props.type === 'number' && 'tnum', className)} {...props} />
}

export function Select({ className, ...props }: ComponentProps<'select'>) {
  return (
    <select
      className={cn(
        control,
        'appearance-none bg-[url("data:image/svg+xml;utf8,<svg xmlns=%22http://www.w3.org/2000/svg%22 width=%2212%22 height=%2212%22 viewBox=%220 0 24 24%22 fill=%22none%22 stroke=%22%23888%22 stroke-width=%222%22><path d=%22m6 9 6 6 6-6%22/></svg>")] bg-[length:12px] bg-[right_0.75rem_center] bg-no-repeat pr-8',
        className,
      )}
      {...props}
    />
  )
}

export function Textarea({ className, ...props }: ComponentProps<'textarea'>) {
  return <textarea className={cn(control, 'min-h-24 resize-y', className)} {...props} />
}

export function Checkbox({ label, className, ...props }: ComponentProps<'input'> & { label: ReactNode }) {
  return (
    <label className={cn('inline-flex cursor-pointer items-center gap-2 text-sm text-ink', className)}>
      <input type="checkbox" className="size-4 accent-accent" {...props} />
      {label}
    </label>
  )
}
