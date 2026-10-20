import { useNavigate } from '@tanstack/react-router'
import { Command } from 'cmdk'
import { Home, LogOut } from 'lucide-react'
import { type ReactNode, useEffect, useState } from 'react'
import { useAuth } from 'react-oidc-context'
import { Dialog } from 'radix-ui'

interface Action {
  label: string
  icon: ReactNode
  go: () => void
}

export function CommandPalette({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const navigate = useNavigate()
  const auth = useAuth()
  const [query, setQuery] = useState('')

  const setOpen = (next: boolean) => {
    if (!next) setQuery('')
    onOpenChange(next)
  }

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault()
        setOpen(!open)
      }
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  })

  const run = (action: () => void) => {
    setOpen(false)
    action()
  }

  const goTo: Action[] = [{ label: 'Sample', icon: <Home className="size-4" />, go: () => void navigate({ to: '/' }) }]
  const account: Action[] = [
    { label: 'Sign out', icon: <LogOut className="size-4" />, go: () => void auth.signoutRedirect() },
  ]

  const needle = query.trim().toLowerCase()
  const matching = (actions: Action[]) => actions.filter((a) => a.label.toLowerCase().includes(needle))
  const actionGroups = [
    { heading: 'Go to', actions: matching(goTo) },
    { heading: 'Account', actions: matching(account) },
  ].filter((g) => g.actions.length > 0)

  return (
    <Dialog.Root open={open} onOpenChange={setOpen}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-ink/20 backdrop-blur-[2px]" />
        <Dialog.Content
          aria-label="Command palette"
          className="fixed top-[12vh] left-1/2 z-50 w-[min(640px,92vw)] -translate-x-1/2 overflow-hidden rounded-xl border border-line bg-surface-raised shadow-pop"
        >
          <Dialog.Title className="sr-only">Search or jump to</Dialog.Title>
          <Dialog.Description className="sr-only">
            Type to filter the places and actions, then choose one.
          </Dialog.Description>
          <Command label="Command palette" shouldFilter={false}>
            <Command.Input
              value={query}
              onValueChange={setQuery}
              placeholder="Jump to a page or type a command…"
              className="h-12 w-full border-b border-line bg-transparent px-4 text-sm text-ink outline-none placeholder:text-ink-tertiary"
            />
            <Command.List className="max-h-[50vh] overflow-y-auto p-2">
              {actionGroups.length === 0 ? <div className={note}>No matches for “{query}”.</div> : null}
              {actionGroups.map((g) => (
                <Command.Group key={g.heading} heading={g.heading} className={group}>
                  {g.actions.map((a) => (
                    <Item key={a.label} onSelect={() => run(a.go)} icon={a.icon} label={a.label} />
                  ))}
                </Command.Group>
              ))}
            </Command.List>
          </Command>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

const note = 'px-2 py-6 text-center text-sm text-ink-tertiary'

const group =
  'mb-1 text-2xs font-medium tracking-[0.08em] text-ink-tertiary uppercase [&_[cmdk-group-heading]]:px-2 [&_[cmdk-group-heading]]:py-1.5'

function Item({ onSelect, icon, label }: { onSelect: () => void; icon: ReactNode; label: string }) {
  return (
    <Command.Item
      value={label}
      onSelect={onSelect}
      className="flex cursor-pointer items-center gap-2.5 rounded-md px-2 py-2 text-sm text-ink normal-case tracking-normal data-[selected=true]:bg-surface-sunken [&_svg]:text-ink-tertiary data-[selected=true]:[&_svg]:text-accent"
    >
      {icon}
      <span className="flex-1 truncate">{label}</span>
    </Command.Item>
  )
}
