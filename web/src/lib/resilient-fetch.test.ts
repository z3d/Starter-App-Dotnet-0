import { afterEach, describe, expect, it, vi } from 'vitest'
import { client, resilientFetch, timeouts } from './api'
import { connectionState, NetworkError, resetConnectionForTests } from './connection'

const ok = () => new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } })

describe('resilient fetch', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    resetConnectionForTests()
  })

  it('tries a read once more after a network failure, and clears any outage when it lands', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValueOnce(ok())

    const response = await resilientFetch(new Request('http://localhost/api/v1/customers'))

    expect(response.status).toBe(200)
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(connectionState().outage).toBeNull()
  })

  it('after the second failure explains it from the network state, asked only then', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'))
    const onLine = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)

    await expect(resilientFetch(new Request('http://localhost/api/v1/customers'))).rejects.toBeInstanceOf(NetworkError)

    expect(onLine).toHaveBeenCalledTimes(1)
    expect(connectionState().outage).toBe('offline')
  })

  it('names an unreachable service when the device is online', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'))
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(true)

    await expect(resilientFetch(new Request('http://localhost/api/v1/customers'))).rejects.toMatchObject({
      outage: 'unreachable',
    })
  })

  it('never repeats a write, which may have landed', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'))

    await expect(
      resilientFetch(new Request('http://localhost/api/v1/customers', { method: 'POST', body: '{}' })),
    ).rejects.toBeInstanceOf(NetworkError)
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('retries a gateway error once, then reports the service as unavailable', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async () => new Response(null, { status: 503 }))

    const response = await resilientFetch(new Request('http://localhost/api/v1/customers'))

    expect(response.status).toBe(503)
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(connectionState().outage).toBe('unavailable')
  })

  it('ends a read that hangs, as a timeout', async () => {
    const readMs = timeouts.readMs
    timeouts.readMs = 20
    vi.spyOn(globalThis, 'fetch').mockImplementation(
      (input) =>
        new Promise((_, reject) => {
          const signal = (input as Request).signal
          signal.addEventListener('abort', () => reject(signal.reason))
        }),
    )
    try {
      await expect(resilientFetch(new Request('http://localhost/api/v1/customers'))).rejects.toMatchObject({
        outage: 'timeout',
      })
    } finally {
      timeouts.readMs = readMs
    }
  })

  it('names the build on every call, and hears when that build is stale and what the operator says', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response('[]', {
        status: 200,
        headers: {
          'Content-Type': 'application/json',
          'X-Client-Reload': 'true',
          'X-Service-Notice': 'Payments%20are%20slow',
        },
      }),
    )

    await client.GET('/api/v1/customers')

    expect((fetchMock.mock.calls[0]![0] as Request).headers.get('X-Client-Build')).toBe('dev')
    expect(connectionState()).toMatchObject({ stale: true, notice: 'Payments are slow' })
  })
})
