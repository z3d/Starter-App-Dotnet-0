import { createFileRoute, Outlet } from '@tanstack/react-router'
import { useEffect } from 'react'
import { AppShell } from '@/components/app-shell'
import { FullPageStatus } from '@/components/full-page-status'
import { signIn } from '@/lib/auth'
import { useSession } from '@/lib/session'

// Every authenticated screen lives under this pathless layout route. A visitor with no session is
// sent to sign in through the API; the API still checks every call itself, so this gate is UX,
// not security.
export const Route = createFileRoute('/_app')({
  component: AuthenticatedLayout,
})

function AuthenticatedLayout() {
  const { status, reload } = useSession()

  useEffect(() => {
    if (status === 'signed-out') signIn()
  }, [status])

  if (status === 'unavailable')
    return (
      <FullPageStatus
        title="Sign-in is unavailable"
        detail="The service could not say who is signed in. Try again shortly."
        action={{ label: 'Try again', onClick: () => void reload() }}
      />
    )
  if (status !== 'signed-in') return <FullPageStatus title="Redirecting to sign in…" />

  return (
    <AppShell>
      <Outlet />
    </AppShell>
  )
}
