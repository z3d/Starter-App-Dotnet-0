import { createFileRoute } from '@tanstack/react-router'
import { FullPageStatus } from '@/components/full-page-status'
import { signIn } from '@/lib/auth'

const messages: Record<string, { title: string; detail: string }> = {
  'signed-out': { title: 'You are signed out', detail: 'Your session has ended here and with the sign-in service.' },
  idle: { title: 'Signed out after a while without activity', detail: 'Sign in again to carry on.' },
  expired: { title: 'Your session has ended', detail: 'Sessions last a set time. Sign in again to carry on.' },
  failed: {
    title: 'Sign-in failed',
    detail: 'The sign-in could not be completed. Start again from this page, in the same browser.',
  },
}

export const Route = createFileRoute('/signed-out')({
  validateSearch: (search: Record<string, unknown>) => ({
    reason: typeof search.reason === 'string' ? search.reason : '',
  }),
  component: SignedOut,
})

function SignedOut() {
  const { reason } = Route.useSearch()
  const message = messages[reason] ?? messages['signed-out']!
  return (
    <FullPageStatus
      title={message.title}
      detail={message.detail}
      action={{ label: 'Sign in', onClick: () => signIn('/') }}
    />
  )
}
