import { createFileRoute } from '@tanstack/react-router'
import { useAuth } from 'react-oidc-context'
import { FullPageStatus } from '@/components/full-page-status'

// The identity provider redirects here with ?code=...; react-oidc-context completes the PKCE
// exchange and onSigninCallback (lib/auth.ts) sends the user back to where they started.
export const Route = createFileRoute('/auth/callback')({
  component: AuthCallback,
})

function AuthCallback() {
  const auth = useAuth()
  if (auth.error) return <FullPageStatus title="Sign-in failed" detail={auth.error.message} />
  return <FullPageStatus title="Signing you in…" />
}
