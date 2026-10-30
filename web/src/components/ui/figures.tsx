import type { LucideIcon } from 'lucide-react'
import type { ComponentProps, ReactNode } from 'react'
import { cn } from '@/lib/utils'

export function Statement({ className, children, ...props }: ComponentProps<'section'>) {
  return (
    <section
      className={cn(
        'statement relative overflow-hidden rounded-2xl px-6 py-7 text-rail-ink md:px-8 md:py-8',
        className,
      )}
      {...props}
    >
      <svg
        aria-hidden
        viewBox="0 0 400 200"
        preserveAspectRatio="none"
        className="pointer-events-none absolute inset-x-0 bottom-0 h-2/3 w-full text-highlight"
        fill="none"
        stroke="currentColor"
      >
        {[0, 1, 2, 3, 4].map((i) => (
          <path
            key={i}
            d={`M0 ${String(170 - i * 18)} C 120 ${String(120 - i * 22)}, 260 ${String(210 - i * 14)}, 400 ${String(110 - i * 20)}`}
            strokeWidth="1"
            opacity={0.16 - i * 0.025}
          />
        ))}
      </svg>
      <div className="relative">{children}</div>
    </section>
  )
}

export function HeroFigure({ label, value, hint }: { label: ReactNode; value: ReactNode; hint?: ReactNode }) {
  return (
    <div>
      <p className="inline-flex items-center gap-1.5 text-2xs font-medium tracking-[0.08em] text-highlight uppercase">
        <span className="size-1.5 rounded-full bg-highlight" aria-hidden />
        {label}
      </p>
      <p className="mt-2 font-display text-[4rem] leading-[0.9] tracking-[-0.02em] text-white tnum md:text-[5rem]">
        {value}
      </p>
      {hint ? <p className="mt-3 max-w-md text-sm text-rail-ink-muted">{hint}</p> : null}
    </div>
  )
}

export function FigureStrip({ className, ...props }: ComponentProps<'div'>) {
  return (
    <div
      className={cn(
        'card grid divide-y divide-line overflow-hidden rounded-xl bg-surface sm:auto-cols-fr sm:grid-flow-col sm:divide-x sm:divide-y-0',
        className,
      )}
      {...props}
    />
  )
}

const figureTones = {
  neutral: 'text-ink',
  accent: 'text-accent-deep',
  positive: 'text-positive',
  caution: 'text-caution',
}

export function Figure({
  label,
  value,
  hint,
  icon: Icon,
  tone = 'neutral',
  className,
  dark,
  small,
}: {
  label: ReactNode
  value: ReactNode
  hint?: ReactNode
  icon?: LucideIcon
  tone?: keyof typeof figureTones
  className?: string
  dark?: boolean
  small?: boolean
}) {
  return (
    <div className={cn('min-w-0 px-5 py-4', className)}>
      <p
        className={cn(
          'flex items-center gap-1.5 text-xs font-medium',
          dark ? 'text-rail-ink-muted' : 'text-ink-secondary',
        )}
      >
        {Icon ? <Icon className={cn('size-3.5', dark ? 'text-highlight' : 'text-accent')} aria-hidden /> : null}
        {label}
      </p>
      <p
        className={cn(
          'mt-1 font-display tnum',
          small ? 'text-[1.5rem] leading-tight' : 'text-[2rem] leading-none',
          dark ? 'text-white' : figureTones[tone],
        )}
      >
        {value}
      </p>
      {hint ? <p className={cn('mt-1.5 text-xs', dark ? 'text-rail-ink-muted' : 'text-ink-tertiary')}>{hint}</p> : null}
    </div>
  )
}

const segmentTones = {
  accent: 'bg-accent',
  deep: 'bg-accent-deep',
  highlight: 'bg-highlight',
  positive: 'bg-positive',
  caution: 'bg-caution',
  info: 'bg-info',
  rest: 'bg-line-strong',
}

export type Segment = { label: string; value: number; display: string; tone: keyof typeof segmentTones }

export function SplitBar({ segments, label, dark }: { segments: Segment[]; label: string; dark?: boolean }) {
  const parts = segments.filter((s) => s.value > 0)
  const total = parts.reduce((sum, s) => sum + s.value, 0)
  return (
    <figure aria-label={label} data-testid="split-bar">
      <div
        className={cn(
          'flex h-3 w-full gap-0.5 overflow-hidden rounded-full',
          dark ? 'bg-white/10' : 'bg-surface-sunken',
        )}
      >
        {total > 0
          ? parts.map((s) => (
              <span
                key={s.label}
                className={cn('grow-bar h-full first:rounded-l-full last:rounded-r-full', segmentTones[s.tone])}
                style={{ width: `${String((s.value / total) * 100)}%` }}
              />
            ))
          : null}
      </div>
      <figcaption className="mt-3 flex flex-wrap gap-x-7 gap-y-2">
        {parts.map((s) => (
          <span key={s.label} className="inline-flex items-baseline gap-2 text-sm">
            <span className={cn('size-2.5 shrink-0 translate-y-px rounded-sm', segmentTones[s.tone])} aria-hidden />
            <span className={dark ? 'text-rail-ink-muted' : 'text-ink-secondary'}>{s.label}</span>
            <span className={cn('tnum font-medium', dark ? 'text-white' : 'text-ink')}>{s.display}</span>
          </span>
        ))}
      </figcaption>
    </figure>
  )
}
