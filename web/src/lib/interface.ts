import { useQuery } from '@tanstack/react-query'
import { useSyncExternalStore } from 'react'
import { call, client } from '@/lib/api'

const KEY = 'starterapp.interface'
const listeners = new Set<() => void>()

let unsaved: boolean | null = null
let asked: { text: string; id: number } | null = null
let asks = 0

function stored(): boolean | null {
  try {
    const value = window.localStorage.getItem(KEY)
    return value === 'assistant' ? true : value === 'traditional' ? false : null
  } catch {
    return unsaved
  }
}

function notify() {
  for (const listener of listeners) listener()
}

function remember(open: boolean) {
  unsaved = open
  try {
    window.localStorage.setItem(KEY, open ? 'assistant' : 'traditional')
  } catch {
    // Kept in memory above.
  }
}

export function openAssistant(question?: string) {
  remember(true)
  if (question?.trim()) asked = { text: question.trim(), id: ++asks }
  notify()
}

export function closeAssistant() {
  remember(false)
  notify()
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export const isWide = () => typeof window.matchMedia === 'function' && window.matchMedia('(min-width: 768px)').matches

export function useAssistantStatus() {
  return useQuery({
    queryKey: ['assistant', 'status'],
    queryFn: () => call(client.GET('/api/v1/assistant')),
    staleTime: 5 * 60_000,
    retry: false,
  })
}

export function useAssistantDock(): {
  available: boolean
  open: boolean
  question: { text: string; id: number } | null
} {
  const choice = useSyncExternalStore(subscribe, stored, () => null)
  const question = useSyncExternalStore(
    subscribe,
    () => asked,
    () => null,
  )
  const status = useAssistantStatus().data
  const available = status?.enabled === true
  const usual = status?.primary === true && isWide()
  return { available, open: available && (choice ?? usual), question }
}
