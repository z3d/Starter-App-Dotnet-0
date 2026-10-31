import { AlertTriangle, Check, Info, type LucideIcon, OctagonAlert, Sparkles, X } from 'lucide-react'
import type { ComponentProps, ReactNode } from 'react'
import { cn } from '@/lib/utils'
import { Button } from './button'

// The small set of layout primitives every screen is built from. Kept in one file so the
// vocabulary stays small: PageHeader, Card, StatTile, DataTable, EmptyState, Steps, Avatar, Chip (docs/DESIGN.md).

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
    <header className="relative mb-6 overflow-hidden rounded-xl bg-[linear-gradient(115deg,var(--color-surface)_35%,var(--color-accent-soft)_80%,var(--color-highlight-soft))] px-6 py-6 card md:px-8 md:py-7">
      <HeaderArt />
      <div className="relative flex flex-wrap items-end justify-between gap-4">
        <div className="min-w-0">
          {eyebrow ? (
            <p className="mb-2 inline-flex items-center gap-1.5 text-2xs font-medium tracking-[0.08em] text-accent-deep uppercase">
              <span className="size-1.5 rounded-full bg-highlight" aria-hidden />
              {eyebrow}
            </p>
          ) : null}
          <h1 className="font-display text-[2.5rem] leading-none text-ink">{title}</h1>
          {description ? <p className="mt-2.5 max-w-2xl text-ink-secondary">{description}</p> : null}
        </div>
        {actions ? <div className="flex shrink-0 flex-wrap items-center gap-2">{actions}</div> : null}
      </div>
    </header>
  )
}

// Contour lines in the header's corner, the way a watermark sits behind a statement's figures.
function HeaderArt() {
  return (
    <svg
      aria-hidden
      viewBox="0 0 320 160"
      className="pointer-events-none absolute -top-6 -right-10 h-48 w-96 text-accent"
      fill="none"
      stroke="currentColor"
    >
      {[0, 1, 2, 3, 4, 5].map((i) => (
        <circle key={i} cx="260" cy="40" r={30 + i * 22} strokeWidth="1.2" opacity={0.3 - i * 0.04} />
      ))}
      <circle cx="260" cy="40" r="9" className="fill-highlight stroke-none" opacity="0.75" />
    </svg>
  )
}

export function Card({ className, interactive, ...props }: ComponentProps<'div'> & { interactive?: boolean }) {
  return (
    <div
      className={cn(
        'card rounded-xl bg-surface',
        interactive && 'transition-[box-shadow,transform] duration-150 hover:-translate-y-0.5 hover:shadow-lift',
        className,
      )}
      {...props}
    />
  )
}

// One figure on a card. Prefer a FigureStrip for several figures and a Statement for the screen's hero (docs/DESIGN.md).
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
          tone === 'accent' && 'text-accent-deep',
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
    <div className="card overflow-x-auto rounded-xl bg-surface">
      <table className={cn('w-full border-collapse text-sm', className)} {...props} />
    </div>
  )
}

export function Th({ className, align, ...props }: ComponentProps<'th'> & { align?: 'left' | 'right' }) {
  return (
    <th
      className={cn(
        'border-b border-line bg-surface-sunken/70 px-3 py-2.5 text-2xs font-medium tracking-[0.06em] text-ink-tertiary uppercase first:pl-5 last:pr-5',
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
        'border-t border-line/70 px-3 py-3 align-middle first:pl-5 last:pr-5',
        align === 'right' && 'text-right tnum',
        className,
      )}
      {...props}
    />
  )
}

export function Tr({ className, ...props }: ComponentProps<'tr'>) {
  return <tr className={cn('transition-colors hover:bg-accent-soft/35', className)} {...props} />
}

export function EmptyState({
  title,
  description,
  action,
  icon: Icon = Sparkles,
  compact,
  done,
}: {
  title: ReactNode
  description?: ReactNode
  action?: ReactNode
  icon?: LucideIcon
  compact?: boolean
  done?: boolean
}) {
  if (compact)
    return (
      <div className="flex items-start gap-3 rounded-lg bg-surface-sunken/60 px-4 py-3">
        <span
          className={cn(
            'grid size-8 shrink-0 place-items-center rounded-full',
            done ? 'bg-positive-soft text-positive' : 'bg-accent-soft text-accent',
          )}
          aria-hidden
        >
          <Icon className="size-4" />
        </span>
        <div className="min-w-0">
          <p className="font-medium text-ink">{title}</p>
          {description ? <p className="text-xs text-ink-tertiary">{description}</p> : null}
        </div>
      </div>
    )
  return (
    <div className="flex flex-col items-center rounded-xl border border-dashed border-line-strong bg-[radial-gradient(circle_at_50%_0%,var(--color-accent-soft),transparent_65%)] px-6 py-12 text-center">
      <span
        className="relative mb-4 grid size-14 place-items-center rounded-full bg-surface text-accent card"
        aria-hidden
      >
        <Icon className="size-6" />
      </span>
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
    <div className="card rounded-xl bg-surface p-4" aria-busy>
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

const chipTones = {
  neutral: 'bg-surface-sunken text-ink-secondary',
  positive: 'bg-positive-soft text-positive',
  caution: 'bg-caution-soft text-caution',
  negative: 'bg-negative-soft text-negative',
  info: 'bg-info-soft text-info',
  ai: 'bg-ai-soft text-ai',
  accent: 'bg-accent-soft text-accent',
}

export function Chip({
  tone = 'neutral',
  className,
  children,
  ...props
}: ComponentProps<'span'> & { tone?: keyof typeof chipTones }) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-medium whitespace-nowrap ring-1 ring-current/15 ring-inset',
        chipTones[tone],
        className,
      )}
      {...props}
    >
      <span className="size-1.5 shrink-0 rounded-full bg-current" aria-hidden />
      {children}
    </span>
  )
}

const calloutTones = {
  info: { box: 'bg-info-soft text-info border-info', icon: Info },
  caution: { box: 'bg-caution-soft text-caution border-caution', icon: AlertTriangle },
  negative: { box: 'bg-negative-soft text-negative border-negative', icon: OctagonAlert },
}

export function Callout({ tone = 'info', children }: { tone?: 'info' | 'caution' | 'negative'; children: ReactNode }) {
  const { box, icon: Icon } = calloutTones[tone]
  return (
    <div className={cn('rise flex gap-2.5 rounded-lg border-l-[3px] px-3.5 py-2.5 text-sm', box)}>
      <Icon className="mt-0.5 size-4 shrink-0" aria-hidden />
      <div className="min-w-0">{children}</div>
    </div>
  )
}

export type Step = { label: string; state: 'done' | 'current' | 'upcoming' | 'stopped' }

// A record's way from first step to last; a step that stopped it says so in words beside the colour.
export function Steps({ steps, label }: { steps: Step[]; label: string }) {
  return (
    <ol aria-label={label} className="flex w-full items-start" data-testid="steps">
      {steps.map((step, i) => (
        <li
          key={step.label}
          className="relative flex flex-1 flex-col items-center text-center"
          aria-current={step.state === 'current' ? 'step' : undefined}
        >
          {i > 0 ? (
            <span
              aria-hidden
              className={cn(
                'absolute top-3.5 right-1/2 h-0.5 w-full -translate-y-1/2',
                step.state === 'upcoming' ? 'bg-line' : 'bg-gradient-to-r from-accent to-accent/70',
              )}
            />
          ) : null}
          <span
            className={cn(
              'relative z-10 grid size-7 place-items-center rounded-full text-xs font-medium tnum ring-4 ring-surface',
              step.state === 'done' && 'bg-accent text-accent-ink',
              step.state === 'current' &&
                'bg-highlight text-highlight-ink shadow-[0_0_0_6px_var(--color-highlight-soft)]',
              step.state === 'upcoming' && 'bg-surface-sunken text-ink-tertiary',
              step.state === 'stopped' && 'bg-negative text-surface',
            )}
          >
            {step.state === 'done' ? (
              <Check className="size-3.5" aria-hidden />
            ) : step.state === 'stopped' ? (
              <X className="size-3.5" aria-hidden />
            ) : (
              i + 1
            )}
          </span>
          <span
            className={cn(
              'mt-2 px-1 text-xs',
              step.state === 'current' ? 'font-medium text-ink' : 'text-ink-secondary',
              step.state === 'stopped' && 'font-medium text-negative',
            )}
          >
            {step.label}
          </span>
        </li>
      ))}
    </ol>
  )
}

const avatarHues = [
  'bg-accent-soft text-accent-deep',
  'bg-highlight text-highlight-ink',
  'bg-positive-soft text-positive',
  'bg-info-soft text-info',
  'bg-ai-soft text-ai',
]

export function Avatar({ name, className }: { name: string; className?: string }) {
  const initials = name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('')
  const hue = avatarHues[[...name].reduce((sum, c) => sum + c.charCodeAt(0), 0) % avatarHues.length]
  return (
    <span
      aria-hidden
      className={cn(
        'inline-grid size-7 shrink-0 place-items-center rounded-full text-2xs font-semibold',
        hue,
        className,
      )}
    >
      {initials || '·'}
    </span>
  )
}
