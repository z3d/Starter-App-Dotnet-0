import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it } from 'vitest'
import { modeKey } from '@/lib/appearance'
import { AppearanceMenu } from './appearance-menu'

const root = document.documentElement

afterEach(() => {
  window.localStorage.clear()
  root.removeAttribute('data-theme')
})

describe('AppearanceMenu', () => {
  it('offers System, Light and Dark with the stored one ticked, and switches the page', async () => {
    window.localStorage.setItem(modeKey, 'light')
    const user = userEvent.setup()
    render(<AppearanceMenu />)

    await user.click(screen.getByRole('button', { name: 'Appearance' }))
    expect(screen.getAllByRole('menuitemradio').map((item) => item.textContent)).toEqual(['System', 'Light', 'Dark'])
    expect(screen.getByRole('menuitemradio', { name: 'Light' })).toHaveAttribute('aria-checked', 'true')

    await user.click(screen.getByRole('menuitemradio', { name: 'Dark' }))
    expect(root.dataset.theme).toBe('dark')
    expect(window.localStorage.getItem(modeKey)).toBe('dark')

    await user.click(screen.getByRole('button', { name: 'Appearance' }))
    await user.click(screen.getByRole('menuitemradio', { name: 'System' }))
    expect(root.hasAttribute('data-theme')).toBe(false)
    expect(window.localStorage.getItem(modeKey)).toBeNull()
  })
})
