import { createFileRoute } from '@tanstack/react-router'
import { UserManager } from 'oidc-client-ts'
import { useEffect } from 'react'
import { oidcSettings } from '@/lib/auth'

// Loaded in a hidden iframe by automaticSilentRenew; hands the renewed token back to the parent.
export const Route = createFileRoute('/auth/silent')({
  component: SilentRenew,
})

function SilentRenew() {
  useEffect(() => {
    void new UserManager(oidcSettings).signinSilentCallback()
  }, [])
  return null
}
