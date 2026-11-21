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
vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ user: { profile: { name: 'Dev User', tid: 'tenant' } }, signoutRedirect }),
}))
vi.mock('@/lib/appearance', async (original) => ({
  ...(await original<typeof import('@/lib/appearance')>()),
  useCanAdminister: () => true,
  useTenantAppearance: () => {},
}))
vi.mock('@/components/command-palette', () => ({ CommandPalette: () => null }))
vi.mock('@/components/assistant-dock', () => ({ AssistantDock: () => null }))
const dock = vi.hoisted(() => ({ available: false }))
vi.mock('@/lib/interface', () => ({
  openAssistant: vi.fn(),
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
})
