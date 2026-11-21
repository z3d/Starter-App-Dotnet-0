import { useSyncExternalStore } from 'react'

// Why the service may be out of reach, decided after a call has failed, never before one is made.
export type Outage = 'offline' | 'unreachable' | 'timeout' | 'unavailable'

export interface ConnectionState {
  outage: Outage | null
  notice: string | null
  stale: boolean
}

let state: ConnectionState = { outage: null, notice: null, stale: false }
const listeners = new Set<() => void>()

function set(next: Partial<ConnectionState>) {
  const merged = { ...state, ...next }
  if (merged.outage === state.outage && merged.notice === state.notice && merged.stale === state.stale) return
  state = merged
  listeners.forEach((listener) => listener())
}

export const reportOutage = (outage: Outage) => set({ outage })
export const clearOutage = () => set({ outage: null })
export const setServiceNotice = (notice: string | null) => set({ notice })
export const markStale = () => set({ stale: true })
export const connectionState = () => state

export function useConnection(): ConnectionState {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
    () => state,
  )
}

export function resetConnectionForTests() {
  state = { outage: null, notice: null, stale: false }
}

export const outageMessages: Record<Outage, string> = {
  offline: 'You appear to be offline. Check your connection; the page will carry on once it is back.',
  unreachable: 'The service could not be reached. Try again in a moment.',
  timeout: 'The service took too long to answer. Try again in a moment.',
  unavailable: 'The service is having trouble right now. Try again in a moment.',
}

export class NetworkError extends Error {
  readonly outage: Outage

  constructor(outage: Outage) {
    super(outageMessages[outage])
    this.name = 'NetworkError'
    this.outage = outage
  }
}
