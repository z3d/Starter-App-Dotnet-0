import { fireEvent, render, screen, within } from '@testing-library/react'
import type { ReactNode } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { AppShell } from './app-shell'

const signoutRedirect = vi.fn()
vi.mock('@tanstack/react-router', () => ({
  Link: ({ to, children, ...props }: { to: string; children: ReactNode }) => (
    <a href={to} onClick={(event) => event.preventDefault()} {...props}>
      {children}
    </a>
  ),
  useRouterState: () => '/',
}))
vi.mock('@/lib/session', () => ({
  useSession: () => ({
    displayName: 'Dev User',
    session: { tenantId: 'tenant' },
    scopes: [],
    signOut: signoutRedirect,
  }),
}))
vi.mock('@/lib/appearance', async (original) => ({
  ...(await original<typeof import('@/lib/appearance')>()),
  useCanAdminister: () => true,
  useTenantAppearance: () => {},
}))
vi.mock('@/components/command-palette', () => ({ CommandPalette: () => null }))
vi.mock('@/components/assistant-dock', () => ({ AssistantDock: () => null }))
const dock = vi.hoisted(() => ({ available: false }))
const openAssistant = vi.hoisted(() => vi.fn())
vi.mock('@/lib/interface', () => ({
  openAssistant,
  useAssistantDock: () => ({ available: dock.available, open: false, question: null }),
}))

describe('AppShell', () => {
  it('puts the side menu behind a menu button in the phone header, closing when a page is chosen', () => {
    render(<AppShell>page</AppShell>)
    const header = screen.getByRole('banner')
    expect(within(header).queryByRole('button', { name: 'Sign out' })).toBeNull()
    fireEvent.click(within(header).getByRole('button', { name: 'Menu' }))
    const menu = screen.getByRole('dialog', { name: 'Menu' })
    expect(within(menu).getByRole('navigation', { name: 'Primary' })).toBeInTheDocument()
    fireEvent.click(within(menu).getByRole('button', { name: 'Sign out' }))
    expect(signoutRedirect).toHaveBeenCalled()
    fireEvent.click(within(menu).getByRole('link', { name: 'Settings' }))
    expect(screen.queryByRole('dialog', { name: 'Menu' })).toBeNull()
  })

  it('offers the assistant on the rail only when the API says it is on', () => {
    dock.available = false
    const { unmount } = render(<AppShell>page</AppShell>)
    expect(screen.queryByRole('button', { name: 'Use the assistant' })).toBeNull()
    unmount()

    dock.available = true
    render(<AppShell>page</AppShell>)
    fireEvent.click(screen.getByRole('button', { name: 'Use the assistant' }))
    expect(openAssistant).toHaveBeenCalled()
  })
})
