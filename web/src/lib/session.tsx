import { createContext, type ReactNode, useCallback, useContext, useEffect, useState } from 'react'
import { client } from './api'
import { setCsrfToken, signIn, signOut } from './auth'
import type { components } from './api.generated'

export type BrowserSession = components['schemas']['BrowserSessionResponse']

export interface SessionState {
  status: 'loading' | 'signed-in' | 'signed-out' | 'unavailable'
  session: BrowserSession | null
  scopes: string[]
  displayName: string
  signIn: () => void
  signOut: () => void
  reload: () => Promise<void>
}

const SessionContext = createContext<SessionState | null>(null)

export function SessionProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<SessionState['status']>('loading')
  const [session, setSession] = useState<BrowserSession | null>(null)

  const reload = useCallback(async () => {
    try {
      const { data, response } = await client.GET('/api/auth/session')
      if (response.ok && data) {
        setCsrfToken(data.csrfToken)
        setSession(data)
        setStatus('signed-in')
      } else {
        setCsrfToken(null)
        setSession(null)
        setStatus(response.status === 401 ? 'signed-out' : 'unavailable')
      }
    } catch {
      setStatus('unavailable')
    }
  }, [])

  useEffect(() => {
    void reload()
  }, [reload])

  const value: SessionState = {
    status,
    session,
    scopes: (session?.scope ?? '').split(' ').filter(Boolean),
    displayName: session?.displayName || session?.subject || 'You',
    signIn: () => signIn(),
    signOut: () => signOut(),
    reload,
  }
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>
}

export function useSession(): SessionState {
  const value = useContext(SessionContext)
  if (!value) throw new Error('useSession is used outside SessionProvider')
  return value
}
