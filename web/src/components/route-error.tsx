import { type ErrorComponentProps, Link, useRouter } from '@tanstack/react-router'
import { useEffect } from 'react'
import { Button } from '@/components/ui/button'
import { ApiError } from '@/lib/api'
import { reportError } from '@/lib/client-events'

// What a person sees when a screen throws: what failed, a reference support can find, and a way back.
export function RouteError({ error, reset }: ErrorComponentProps) {
  const router = useRouter()
  useEffect(() => reportError(error), [error])
  const reference = error instanceof ApiError ? error.correlationId : null
  const message = error instanceof Error ? error.message : ''

  return (
    <main role="alert" className="flex min-h-svh items-center justify-center bg-canvas p-6 text-ink">
      <div className="max-w-md text-center">
        <p className="font-display text-2xl">This page could not be shown</p>
        <p className="mt-2 text-sm text-ink-secondary">{message || 'Something went wrong.'}</p>
        {reference ? <p className="mt-2 font-mono text-xs text-ink-secondary">Reference {reference}</p> : null}
        <div className="mt-6 flex justify-center gap-3">
          <Button
            variant="primary"
            onClick={() => {
              reset()
              void router.invalidate()
            }}
          >
            Try again
          </Button>
          <Button asChild>
            <Link to="/">Go to the home page</Link>
          </Button>
        </div>
      </div>
    </main>
  )
}
