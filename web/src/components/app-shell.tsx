import { Link, useRouterState } from '@tanstack/react-router'
import { Home, LogOut, Search, ShieldCheck } from 'lucide-react'
import { type ReactNode, useState } from 'react'
import { useAuth } from 'react-oidc-context'
import { CommandPalette } from '@/components/command-palette'
import { cn } from '@/lib/utils'

// The rail. A derived project replaces the sample item with its own pages.
export const nav = [{ to: '/', label: 'Sample', icon: Home, exact: true, mobile: true }] as const

export function AppShell({ children }: { children: ReactNode }) {
  const auth = useAuth()
  const [paletteOpen, setPaletteOpen] = useState(false)
  const pathname = useRouterState({ select: (s) => s.location.pathname })
  const name =
    (auth.user?.profile.name as string | undefined) ??
    (auth.user?.profile.preferred_username as string | undefined) ??
    (auth.user?.profile.sub as string | undefined) ??
    'You'
  const initials = name
    .split(/[\s._-]+/)
    .slice(0, 2)
    .map((s) => s[0]?.toUpperCase() ?? '')
    .join('')

  return (
    <div className="flex min-h-svh text-ink">
      <aside className="rail-lit sticky top-0 hidden h-svh w-60 shrink-0 flex-col overflow-hidden text-rail-ink md:flex">
        <div className="flex h-14 items-center gap-2 px-4">
          <Mark />
          <span className="font-display text-xl tracking-tight">StarterApp</span>
        </div>

        <button
          type="button"
          onClick={() => setPaletteOpen(true)}
          className="mx-3 flex h-9 items-center gap-2 rounded-md border border-rail-line bg-rail-active/40 px-2.5 text-sm text-rail-ink-muted transition-colors hover:bg-rail-active hover:text-rail-ink"
        >
          <Search className="size-4" />
          <span className="flex-1 text-left">Search or jump to…</span>
          <span className="rounded border border-rail-line px-1 font-mono text-2xs">⌘K</span>
        </button>

        <nav className="mt-4 flex flex-col gap-0.5 px-3" aria-label="Primary">
          {nav.map((item) => {
            const active = item.exact ? pathname === item.to : pathname.startsWith(item.to)
            return (
              <Link
                key={item.to}
                to={item.to}
                className={cn(
                  'relative flex h-9 items-center gap-2.5 rounded-md px-2.5 text-sm transition-colors',
                  active
                    ? 'bg-rail-active text-rail-ink shadow-[inset_0_1px_0_oklch(1_0_0/0.05)] before:absolute before:inset-y-2 before:-left-3 before:w-1 before:rounded-r-full before:bg-highlight'
                    : 'text-rail-ink-muted hover:bg-rail-active/60 hover:text-rail-ink',
                )}
              >
                <item.icon className={cn('size-4', active && 'text-highlight')} />
                {item.label}
              </Link>
            )
          })}
        </nav>

        <div className="mx-3 mt-6 rounded-lg border border-rail-line bg-gradient-to-br from-rail-active/70 to-transparent p-3 text-xs text-rail-ink-muted">
          <div className="flex items-center gap-1.5 text-rail-ink">
            <ShieldCheck className="size-3.5 text-highlight" /> Zero trust
          </div>
          <p className="mt-1">The API verifies every token itself; this app only holds it.</p>
        </div>

        <div className="mt-auto border-t border-rail-line p-3">
          <div className="flex items-center gap-2.5">
            <span className="grid size-8 place-items-center rounded-full bg-gradient-to-br from-accent to-highlight text-xs font-semibold text-accent-ink">
              {initials || 'Y'}
            </span>
            <div className="min-w-0 flex-1">
              <p className="truncate text-sm text-rail-ink">{name}</p>
              <p className="truncate text-2xs text-rail-ink-muted" data-testid="tenant-id">
                {auth.user?.profile.tid as string}
              </p>
            </div>
            <button
              type="button"
              aria-label="Sign out"
              onClick={() => void auth.signoutRedirect()}
              className="rounded-md p-1.5 text-rail-ink-muted transition-colors hover:bg-rail-active hover:text-rail-ink"
            >
              <LogOut className="size-4" />
            </button>
          </div>
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-10 flex h-14 items-center gap-3 border-b border-line bg-canvas/85 px-4 backdrop-blur md:hidden">
          <Mark />
          <span className="font-display text-lg">StarterApp</span>
          <button
            type="button"
            onClick={() => setPaletteOpen(true)}
            className="ml-auto rounded-md p-2 text-ink-secondary hover:bg-surface-sunken"
            aria-label="Search"
          >
            <Search className="size-5" />
          </button>
        </header>

        <main key={pathname} className="rise mx-auto w-full max-w-6xl flex-1 px-4 py-6 md:px-8 md:py-8">
          {children}
        </main>

        <nav
          className="sticky bottom-0 flex border-t border-line bg-surface/90 shadow-[0_-8px_24px_-16px_oklch(0_0_0/0.3)] backdrop-blur md:hidden"
          aria-label="Primary"
        >
          {nav
            .filter((item) => item.mobile)
            .map((item) => {
              const active = item.exact ? pathname === item.to : pathname.startsWith(item.to)
              return (
                <Link
                  key={item.to}
                  to={item.to}
                  className={cn(
                    'flex flex-1 flex-col items-center gap-1 py-2 text-2xs',
                    active ? 'font-medium text-accent' : 'text-ink-secondary',
                  )}
                >
                  <item.icon className="size-5" />
                  {item.label}
                </Link>
              )
            })}
        </nav>
      </div>

      <CommandPalette open={paletteOpen} onOpenChange={setPaletteOpen} />
    </div>
  )
}

// A plain mark: three strokes on the accent, lit by the highlight. A derived project draws its own.
function Mark() {
  return (
    <svg width="24" height="24" viewBox="0 0 22 22" aria-hidden className="shrink-0 drop-shadow-sm">
      <defs>
        <linearGradient id="mark" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="var(--accent)" />
          <stop offset="1" stopColor="var(--accent-deep)" />
        </linearGradient>
      </defs>
      <rect x="1" y="1" width="20" height="20" rx="5" fill="url(#mark)" />
      <circle cx="17" cy="5" r="2.6" fill="var(--highlight)" />
      <path d="M6 15.5V6.5M11 15.5V9M16 15.5V4.5" stroke="var(--accent-ink)" strokeWidth="2.2" strokeLinecap="round" />
    </svg>
  )
}
