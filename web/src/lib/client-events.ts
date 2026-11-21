import { client } from './api'
import { build } from './build'

export interface ClientEvent {
  kind: 'error' | 'event'
  name: string
  message?: string
  route: string
  build: string
  elapsedMs: number
}

const batchSize = 20
const perPage = 100
const queue: ClientEvent[] = []
let queuedThisPage = 0
let timer: ReturnType<typeof setTimeout> | undefined

function enqueue(event: Omit<ClientEvent, 'build' | 'elapsedMs' | 'route'> & { route?: string }) {
  if (queuedThisPage >= perPage) return
  queuedThisPage++
  queue.push({
    ...event,
    route: event.route ?? window.location.pathname,
    build,
    // The page's own clock: a device's wall clock may be wrong, the time since the page opened is not.
    elapsedMs: Math.round(performance.now()),
  })
  timer ??= setTimeout(() => void flush(), 2_000)
}

export function reportError(error: unknown, route?: string) {
  const name = error instanceof Error ? error.name : 'Error'
  const message = error instanceof Error ? error.message : String(error)
  enqueue({ kind: 'error', name, message: message.slice(0, 1_000), route })
}

export function reportEvent(name: string, route?: string) {
  enqueue({ kind: 'event', name, route })
}

// A report that cannot be sent is dropped: reporting must never raise an error of its own.
export async function flush(keepalive = false) {
  clearTimeout(timer)
  timer = undefined
  while (queue.length > 0) {
    const events = queue.splice(0, batchSize)
    try {
      await client.POST('/api/v1/client-events', { body: { events }, keepalive })
    } catch {
      return
    }
  }
}

export function installErrorReporting() {
  window.addEventListener('error', (event) => reportError(event.error ?? event.message))
  window.addEventListener('unhandledrejection', (event) => reportError(event.reason))
  window.addEventListener('pagehide', () => void flush(true))
}

export function resetForTests() {
  queue.length = 0
  queuedThisPage = 0
  clearTimeout(timer)
  timer = undefined
}
