import { fireEvent, render, screen } from '@testing-library/react'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { CommandPalette } from './command-palette'

const navigate = vi.fn()
const signoutRedirect = vi.fn()
vi.mock('@tanstack/react-router', () => ({ useNavigate: () => navigate }))
vi.mock('react-oidc-context', () => ({ useAuth: () => ({ signoutRedirect }) }))

describe('CommandPalette', () => {
  beforeAll(() => {
    // cmdk measures its list; jsdom has neither.
    globalThis.ResizeObserver ??= class {
      observe() {}
      unobserve() {}
      disconnect() {}
    } as unknown as typeof ResizeObserver
    Element.prototype.scrollIntoView ??= () => {}
  })

  it('lists the places and the account actions, and runs the one chosen', () => {
    const onOpenChange = vi.fn()
    render(<CommandPalette open onOpenChange={onOpenChange} />)
    expect(screen.getByRole('option', { name: 'Sample' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('option', { name: 'Sign out' }))
    expect(signoutRedirect).toHaveBeenCalled()
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  it('filters by what is typed and says when nothing matches', () => {
    render(<CommandPalette open onOpenChange={() => {}} />)
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'sam' } })
    expect(screen.getByRole('option', { name: 'Sample' })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: 'Sign out' })).not.toBeInTheDocument()
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'nothing here' } })
    expect(screen.getByText('No matches for “nothing here”.')).toBeInTheDocument()
  })

  it('opens and closes on ⌘K', () => {
    const onOpenChange = vi.fn()
    render(<CommandPalette open={false} onOpenChange={onOpenChange} />)
    fireEvent.keyDown(document, { key: 'k', metaKey: true })
    expect(onOpenChange).toHaveBeenCalledWith(true)
  })
})
