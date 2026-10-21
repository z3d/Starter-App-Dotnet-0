import { ChevronRight } from 'lucide-react'
import type { ComponentProps, ReactNode } from 'react'
import { cn } from '@/lib/utils'

export type Stage = { key: string; label: string; count: ReactNode; hint?: string; urgent?: boolean }

export function StageFlow({
  stages,
  selected,
  onSelect,
  label,
  className,
}: {
  stages: Stage[]
  selected?: string
  onSelect: (key: string | undefined) => void
  label: string
  className?: string
}) {
  return (
    <nav aria-label={label} className={cn('card mb-6 overflow-x-auto rounded-xl bg-surface', className)}>
      <ol className="flex min-w-max sm:min-w-0">
        {stages.map((stage, i) => {
          const active = stage.key === selected
          return (
            <li key={stage.key} className="flex min-w-36 flex-1 items-stretch">
              {i > 0 ? <ChevronRight className="my-auto size-4 shrink-0 text-line-strong" aria-hidden /> : null}
              <button
                type="button"
                aria-pressed={active}
                onClick={() => onSelect(active ? undefined : stage.key)}
                className={cn(
                  'relative flex-1 px-4 py-3.5 text-left transition-colors hover:bg-surface-sunken',
                  active && 'bg-surface-sunken',
                )}
              >
                <span className="flex items-center gap-1.5 text-xs font-medium text-ink-secondary">
                  {stage.urgent ? <span className="size-1.5 rounded-full bg-accent" aria-hidden /> : null}
                  {stage.label}
                </span>
                <span
                  className={cn(
                    'mt-0.5 block font-display text-[1.75rem] leading-none tnum',
                    stage.urgent ? 'text-accent' : 'text-ink',
                  )}
                >
                  {stage.count}
                </span>
                {stage.hint ? <span className="mt-1 block text-2xs text-ink-tertiary">{stage.hint}</span> : null}
                {active ? (
                  <span className="absolute inset-x-3 bottom-0 h-0.5 rounded-full bg-accent" aria-hidden />
                ) : null}
              </button>
            </li>
          )
        })}
      </ol>
    </nav>
  )
}

const waitTones = {
  fresh: 'text-ink-secondary',
  slow: 'text-caution',
  late: 'text-negative',
}

export type WaitTone = keyof typeof waitTones

export function Waited({
  days,
  tone = 'fresh',
  prefix = 'Waiting',
}: {
  days: number
  tone?: WaitTone
  prefix?: string
}) {
  const span = days < 1 ? 'under a day' : days === 1 ? '1 day' : `${String(days)} days`
  return (
    <span className={cn('inline-flex items-center gap-1.5 text-xs whitespace-nowrap tnum', waitTones[tone])}>
      <span aria-hidden className={cn('size-1.5 rounded-full', tone === 'fresh' ? 'bg-line-strong' : 'bg-current')} />
      {prefix} {span}
    </span>
  )
}

export function Queue({ className, ...props }: ComponentProps<'ol'>) {
  return <ol className={cn('-mx-2 divide-y divide-line', className)} {...props} />
}

export function QueueRow({
  title,
  detail,
  next,
  reason,
  waited,
  aside,
}: {
  title: ReactNode
  detail?: ReactNode
  next: ReactNode
  reason?: ReactNode
  waited?: ReactNode
  aside?: ReactNode
}) {
  return (
    <li className="grid grid-cols-[1fr_auto] gap-x-4 gap-y-1 rounded-lg px-2 py-3 transition-colors hover:bg-surface-sunken/60">
      <div className="min-w-0">
        <div className="truncate font-medium text-ink">{title}</div>
        {detail ? <div className="mt-0.5 truncate text-xs text-ink-tertiary">{detail}</div> : null}
      </div>
      <div className="row-span-2 self-center text-right">
        {aside ? <div className="font-medium text-ink tnum">{aside}</div> : null}
        {waited ? <div className="mt-0.5">{waited}</div> : null}
      </div>
      <p className="flex min-w-0 flex-wrap items-baseline gap-x-2 text-sm">
        <span className="inline-flex items-center gap-1 font-medium text-accent">
          <ChevronRight className="size-3.5 translate-y-0.5" aria-hidden />
          {next}
        </span>
        {reason ? <span className="text-ink-secondary">{reason}</span> : null}
      </p>
    </li>
  )
}
