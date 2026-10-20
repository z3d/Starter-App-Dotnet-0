import type { ComponentProps, ReactNode } from 'react'
import { cn } from '@/lib/utils'
import { Button } from './button'

// The small set of layout primitives every screen is built from. Kept in one file so the
// vocabulary stays small: PageHeader, Card, StatTile, DataTable, EmptyState, Skeleton, Kbd, Chip.

export function PageHeader({
  eyebrow,
  title,
  description,
  actions,
}: {
  eyebrow?: ReactNode
  title: ReactNode
  description?: ReactNode
  actions?: ReactNode
}) {
  return (
    <header className="flex flex-wrap items-end justify-between gap-4 pb-6">
      <div className="min-w-0">
        {eyebrow ? (
          <p className="mb-1 text-2xs font-medium tracking-[0.08em] text-ink-tertiary uppercase">{eyebrow}</p>
        ) : null}
        <h1 className="font-display text-[2rem] leading-none text-ink">{title}</h1>
        {description ? <p className="mt-2 max-w-2xl text-ink-secondary">{description}</p> : null}
      </div>
      {actions ? <div className="flex shrink-0 items-center gap-2">{actions}</div> : null}
    </header>
  )
}

export function Card({ className, interactive, ...props }: ComponentProps<'div'> & { interactive?: boolean }) {
  return (
    <div
      className={cn(
        'hairline rounded-lg bg-surface',
        interactive && 'transition-shadow duration-150 hover:shadow-lift',
        className,
      )}
      {...props}
    />
  )
}

export function StatTile({
  label,
  value,
  hint,
  tone,
}: {
  label: string
  value: ReactNode
  hint?: ReactNode
  tone?: 'accent' | 'positive' | 'caution'
}) {
  return (
    <Card className="p-4">
      <p className="text-xs font-medium text-ink-secondary">{label}</p>
      <p
        className={cn(
          'mt-1 font-display text-3xl leading-none tnum',
          tone === 'accent' && 'text-accent',
          tone === 'positive' && 'text-positive',
          tone === 'caution' && 'text-caution',
        )}
      >
        {value}
      </p>
      {hint ? <p className="mt-2 text-xs text-ink-tertiary">{hint}</p> : null}
    </Card>
  )
}

export function DataTable({ className, ...props }: ComponentProps<'table'>) {
  return (
    <div className="hairline overflow-x-auto rounded-lg bg-surface">
      <table className={cn('w-full border-collapse text-sm', className)} {...props} />
    </div>
  )
}

export function Th({ className, align, ...props }: ComponentProps<'th'> & { align?: 'left' | 'right' }) {
  return (
    <th
      className={cn(
        'bg-surface-sunken px-3 py-2 text-2xs font-medium tracking-[0.06em] text-ink-secondary uppercase first:pl-4 last:pr-4',
        align === 'right' ? 'text-right' : 'text-left',
        className,
      )}
      {...props}
    />
  )
}

export function Td({ className, align, ...props }: ComponentProps<'td'> & { align?: 'left' | 'right' }) {
  return (
    <td
      className={cn(
        'border-t border-line px-3 py-2.5 align-middle first:pl-4 last:pr-4',
        align === 'right' && 'text-right tnum',
        className,
      )}
      {...props}
    />
  )
}

export function Tr({ className, ...props }: ComponentProps<'tr'>) {
  return <tr className={cn('transition-colors hover:bg-surface-sunken/60', className)} {...props} />
}

export function EmptyState({
  title,
  description,
  action,
}: {
  title: ReactNode
  description?: ReactNode
  action?: ReactNode
}) {
  return (
    <div className="flex flex-col items-center rounded-lg border border-dashed border-line-strong px-6 py-14 text-center">
      <p className="font-display text-2xl text-ink">{title}</p>
      {description ? <p className="mt-2 max-w-md text-ink-secondary">{description}</p> : null}
      {action ? <div className="mt-5">{action}</div> : null}
    </div>
  )
}

export function ShowMore({ list }: { list: { hasMore: boolean; loadingMore: boolean; loadMore: () => void } }) {
  if (!list.hasMore) return null
  return (
    <div className="mt-3 flex justify-center">
      <Button size="sm" onClick={list.loadMore} disabled={list.loadingMore}>
        {list.loadingMore ? 'Loading…' : 'Show more'}
      </Button>
    </div>
  )
}

export function Skeleton({ className }: { className?: string }) {
  return <div className={cn('skeleton h-4 w-full', className)} aria-hidden />
}

export function TableSkeleton({ rows = 5, cols = 4 }: { rows?: number; cols?: number }) {
  return (
    <div className="hairline rounded-lg bg-surface p-4" aria-busy>
      {Array.from({ length: rows }).map((_, r) => (
        <div key={r} className="flex gap-4 py-2">
          {Array.from({ length: cols }).map((__, c) => (
            <Skeleton key={c} className={c === 0 ? 'w-2/5' : 'w-1/5'} />
          ))}
        </div>
      ))}
    </div>
  )
}

export function Kbd({ children }: { children: ReactNode }) {
  return (
    <kbd className="rounded border border-line bg-surface-sunken px-1.5 py-0.5 font-mono text-2xs text-ink-secondary">
      {children}
    </kbd>
  )
}

export function Chip({
  tone = 'neutral',
  className,
  ...props
}: ComponentProps<'span'> & { tone?: 'neutral' | 'positive' | 'caution' | 'negative' | 'info' | 'ai' | 'accent' }) {
  const tones = {
    neutral: 'bg-surface-sunken text-ink-secondary',
    positive: 'bg-positive-soft text-positive',
    caution: 'bg-caution-soft text-caution',
    negative: 'bg-negative-soft text-negative',
    info: 'bg-info-soft text-info',
    ai: 'bg-ai-soft text-ai',
    accent: 'bg-accent-soft text-accent',
  }
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium',
        tones[tone],
        className,
      )}
      {...props}
    />
  )
}

export function Callout({ tone = 'info', children }: { tone?: 'info' | 'caution' | 'negative'; children: ReactNode }) {
  const tones = {
    info: 'bg-info-soft text-info',
    caution: 'bg-caution-soft text-caution',
    negative: 'bg-negative-soft text-negative',
  }
  return <div className={cn('rounded-md px-3 py-2 text-sm', tones[tone])}>{children}</div>
}
