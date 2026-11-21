import { act, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { closeAssistant, openAssistant, useAssistantDock } from './interface'

const status = vi.hoisted(() => ({ enabled: false, primary: false }))

vi.mock('@/lib/api', () => ({
  client: { GET: () => Promise.resolve({ data: { ...status }, response: new Response() }) },
  call: async (promise: Promise<{ data: unknown }>) => (await promise).data,
}))

function wrapper({ children }: { children: ReactNode }) {
  return createElement(QueryClientProvider, { client: new QueryClient() }, children)
}

const wide = (matches: boolean) =>
  vi.stubGlobal('matchMedia', (query: string) => ({ matches, media: query }) as MediaQueryList)

describe('the assistant dock', () => {
  beforeEach(() => {
    window.localStorage.clear()
    status.enabled = false
    status.primary = false
  })
  afterEach(() => vi.unstubAllGlobals())

  it('is not there at all when the API says the assistant is off', async () => {
    wide(true)
    const { result } = renderHook(() => useAssistantDock(), { wrapper })

    await waitFor(() => expect(result.current.available).toBe(false))
    act(() => openAssistant())
    expect(result.current.open).toBe(false)
  })

  it('is only a button when on and not chosen as the way in, and opens when asked', async () => {
    status.enabled = true
    wide(true)
    const { result } = renderHook(() => useAssistantDock(), { wrapper })

    await waitFor(() => expect(result.current.available).toBe(true))
    expect(result.current.open).toBe(false)
    act(() => openAssistant('Show me our products'))
    expect(result.current.open).toBe(true)
    expect(result.current.question?.text).toBe('Show me our products')
  })

  it('starts open on a wide screen where the deployment chose it, and remembers the person closing it', async () => {
    status.enabled = true
    status.primary = true
    wide(true)
    const { result, unmount } = renderHook(() => useAssistantDock(), { wrapper })

    await waitFor(() => expect(result.current.open).toBe(true))
    act(() => closeAssistant())
    expect(result.current.open).toBe(false)
    unmount()

    const again = renderHook(() => useAssistantDock(), { wrapper })
    await waitFor(() => expect(again.result.current.available).toBe(true))
    expect(again.result.current.open).toBe(false)
  })
})
