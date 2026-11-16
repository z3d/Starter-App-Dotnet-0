import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { paletteKey } from '@/lib/appearance'
import { palettes } from '@/palettes'
import { PalettePicker } from './palette-picker'

vi.mock('sonner', () => ({ toast: { success: vi.fn() } }))

const root = document.documentElement

function api(saved: { palette: string | null; updatedBy?: string; lastUpdated?: string }) {
  const sent: Array<{ method: string; body: unknown }> = []
  vi.stubGlobal('fetch', async (request: Request) => {
    const body = request.method === 'PUT' ? await request.json() : undefined
    sent.push({ method: request.method, body })
    const reply =
      request.method === 'PUT'
        ? { palette: (body as { palette: string }).palette, updatedBy: 'admin-1', lastUpdated: '2026-11-17T01:00:00Z' }
        : saved
    return new Response(JSON.stringify(reply), { status: 200, headers: { 'Content-Type': 'application/json' } })
  })
  return sent
}

function renderPicker() {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <PalettePicker />
    </QueryClientProvider>,
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
  window.localStorage.clear()
  root.removeAttribute('data-palette')
})

describe('PalettePicker', () => {
  it("lists every declared theme with the tenant's own marked in use, and says who changed it", async () => {
    api({ palette: 'fresh', updatedBy: 'admin-0', lastUpdated: '2026-11-16T23:00:00Z' })
    renderPicker()

    expect(await screen.findByTestId('appearance-changed')).toHaveTextContent('Last changed by admin-0')
    expect(screen.getAllByRole('radio')).toHaveLength(palettes.length)
    expect(screen.getByRole('radio', { name: /Fresh/ })).toHaveAttribute('aria-checked', 'true')
    expect(screen.getByRole('radio', { name: /Fresh/ })).toHaveTextContent('in use')
    expect(root.dataset.palette).toBe('fresh')
    expect(screen.getByRole('button', { name: 'Save for everyone' })).toBeDisabled()
  })

  it('saves the chosen theme for the tenant and repaints the page in it', async () => {
    const sent = api({ palette: null })
    const user = userEvent.setup()
    renderPicker()

    await waitFor(() => expect(sent).toHaveLength(1))
    await user.click(screen.getByRole('radio', { name: /Sky/ }))
    await user.click(screen.getByRole('button', { name: 'Save for everyone' }))

    await waitFor(() => expect(root.dataset.palette).toBe('sky'))
    expect(sent.at(-1)).toEqual({ method: 'PUT', body: { palette: 'sky' } })
    expect(window.localStorage.getItem(paletteKey)).toBe('sky')
    expect(await screen.findByTestId('appearance-changed')).toHaveTextContent('Last changed by admin-1')
  })
})
