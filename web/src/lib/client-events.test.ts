import { afterEach, describe, expect, it, vi } from 'vitest'
import { flush, reportError, reportEvent, resetForTests } from './client-events'

const posted = async (mock: { mock: { calls: unknown[][] } }, i = 0) => {
  const request = mock.mock.calls[i]![0] as Request
  return { request, body: await request.json() }
}

describe('client events', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    resetForTests()
  })

  it('posts errors and events in one batch to our own API, with the route, the build and the page clock', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 202 }))
    reportError(new TypeError('x is undefined'), '/orders')
    reportEvent('settings-opened', '/settings')
    await flush()

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const { request, body } = await posted(fetchMock)
    expect(request.method).toBe('POST')
    expect(new URL(request.url).pathname).toBe('/api/v1/client-events')
    expect(body.events).toEqual([
      expect.objectContaining({
        kind: 'error',
        name: 'TypeError',
        message: 'x is undefined',
        route: '/orders',
        build: 'dev',
      }),
      expect.objectContaining({ kind: 'event', name: 'settings-opened', route: '/settings', build: 'dev' }),
    ])
    expect(body.events[0].elapsedMs).toBeGreaterThanOrEqual(0)
  })

  it('drops a report it cannot send instead of raising an error of its own', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'))
    reportError(new Error('boom'))
    await expect(flush()).resolves.toBeUndefined()
  })

  it('stops queueing after a hundred reports from one page', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 202 }))
    for (let i = 0; i < 150; i++) reportError(new Error(`e${i}`))
    await flush()
    const sizes = await Promise.all(
      fetchMock.mock.calls.map(async (_, i) => (await posted(fetchMock, i)).body.events.length),
    )
    expect(sizes.reduce((a, b) => a + b, 0)).toBe(100)
  })
})
