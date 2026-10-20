import { createFileRoute, Outlet } from '@tanstack/react-router'
import { useEffect } from 'react'
import { useAuth } from 'react-oidc-context'
import { AppShell } from '@/components/app-shell'
import { FullPageStatus } from '@/components/full-page-status'
import { rememberReturnTo } from '@/lib/auth'

// Every authenticated screen lives under this pathless layout route. Unauthenticated visitors are
// sent to the identity provider; the API still validates every token itself, so this gate is UX,
// not security.
export const Route = createFileRoute('/_app')({
  component: AuthenticatedLayout,
})

function AuthenticatedLayout() {
  const auth = useAuth()

  useEffect(() => {
    if (!auth.isLoading && !auth.isAuthenticated && !auth.activeNavigator && !auth.error) {
      rememberReturnTo()
      void auth.signinRedirect()
    }
  }, [auth])

  if (auth.error) return <FullPageStatus title="Sign-in failed" detail={auth.error.message} />
  if (!auth.isAuthenticated) return <FullPageStatus title="Redirecting to sign in…" />

  return (
    <AppShell>
      <Outlet />
    </AppShell>
  )
}
