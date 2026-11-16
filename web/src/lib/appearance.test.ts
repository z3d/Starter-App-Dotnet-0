import { afterEach, describe, expect, it, vi } from 'vitest'
import { defaultPalette } from '@/palettes'
import { applyMode, applyPalette, currentPalette, modeKey, paletteKey, storedMode } from './appearance'

const root = document.documentElement

afterEach(() => {
  vi.restoreAllMocks()
  window.localStorage.clear()
  root.removeAttribute('data-theme')
  root.removeAttribute('data-palette')
})

describe('light or dark', () => {
  it('sets the mode on the page and keeps it in this browser, and System leaves it to the device', () => {
    applyMode('dark')
    expect(root.dataset.theme).toBe('dark')
    expect(window.localStorage.getItem(modeKey)).toBe('dark')
    expect(storedMode()).toBe('dark')

    applyMode('system')
    expect(root.hasAttribute('data-theme')).toBe(false)
    expect(window.localStorage.getItem(modeKey)).toBeNull()
    expect(storedMode()).toBe('system')
  })

  it('reads anything it did not write as System', () => {
    window.localStorage.setItem(modeKey, 'sepia')
    expect(storedMode()).toBe('system')
  })

  it('still changes the page when the browser refuses storage', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('blocked', 'SecurityError')
    })
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('blocked', 'SecurityError')
    })
    applyMode('light')
    expect(root.dataset.theme).toBe('light')
    expect(storedMode()).toBe('system')
  })
})

describe("the tenant's colour theme", () => {
  it('marks the page with a theme and remembers it for the next first paint', () => {
    applyPalette('sky')
    expect(root.dataset.palette).toBe('sky')
    expect(window.localStorage.getItem(paletteKey)).toBe('sky')
    expect(currentPalette()).toBe('sky')
  })

  it('takes the default as the bare page, and an unknown or missing theme as the default', () => {
    applyPalette('sky')
    applyPalette(defaultPalette)
    expect(root.hasAttribute('data-palette')).toBe(false)
    expect(window.localStorage.getItem(paletteKey)).toBeNull()

    applyPalette('no-such-theme')
    expect(currentPalette()).toBe(defaultPalette)
    applyPalette(null)
    expect(currentPalette()).toBe(defaultPalette)
  })
})
