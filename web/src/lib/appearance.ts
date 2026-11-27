import { useQuery } from '@tanstack/react-query'
import { useEffect } from 'react'
import { useSession } from '@/lib/session'
import { defaultPalette, type PaletteId, palettes } from '@/palettes'
import { call, client } from './api'

// public/appearance.js reads these two keys before the first paint.
export type Mode = 'system' | 'light' | 'dark'
export const modeKey = 'appearance.mode'
export const paletteKey = 'appearance.palette'
export const appearanceQueryKey = ['settings', 'appearance'] as const

function read(key: string): string | null {
  try {
    return window.localStorage.getItem(key)
  } catch {
    return null
  }
}

function write(key: string, value: string | null) {
  try {
    if (value === null) window.localStorage.removeItem(key)
    else window.localStorage.setItem(key, value)
  } catch {
    // Blocked storage: the choice holds for this page only.
  }
}

export function storedMode(): Mode {
  const mode = read(modeKey)
  return mode === 'light' || mode === 'dark' ? mode : 'system'
}

export function applyMode(mode: Mode) {
  const root = document.documentElement
  if (mode === 'system') root.removeAttribute('data-theme')
  else root.setAttribute('data-theme', mode)
  write(modeKey, mode === 'system' ? null : mode)
}

export function isPalette(id: unknown): id is PaletteId {
  return palettes.some((p) => p.id === id)
}

export function currentPalette(): PaletteId {
  const shown = document.documentElement.getAttribute('data-palette')
  return isPalette(shown) ? shown : defaultPalette
}

export function applyPalette(id: string | null | undefined) {
  const palette = isPalette(id) ? id : defaultPalette
  const root = document.documentElement
  if (palette === defaultPalette) root.removeAttribute('data-palette')
  else root.setAttribute('data-palette', palette)
  write(paletteKey, palette === defaultPalette ? null : palette)
}

export const settingsWriteScope = 'settings:write'

export function useCanAdminister(): boolean {
  return useSession().scopes.includes(settingsWriteScope)
}

export function useTenantAppearance() {
  const query = useQuery({
    queryKey: appearanceQueryKey,
    queryFn: () => call(client.GET('/api/v1/settings/appearance')),
  })
  useEffect(() => {
    if (query.data) applyPalette(query.data.palette)
  }, [query.data])
  return query
}
