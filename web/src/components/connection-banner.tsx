import { Button } from '@/components/ui/button'
import { outageMessages, useConnection } from '@/lib/connection'

// A stale build, an outage, or an operator's notice: one line under the header, never a blocking dialog.
export function ConnectionBanner() {
  const { outage, notice, stale } = useConnection()
  if (!stale && !outage && !notice) return null

  return (
    <div
      role="status"
      className="flex flex-col gap-1 border-b border-line bg-surface-sunken px-4 py-2 text-sm text-ink"
    >
      {stale ? (
        <p className="flex flex-wrap items-center gap-2">
          <span>A newer version of this app is available.</span>
          <Button size="sm" variant="primary" onClick={() => window.location.reload()}>
            Reload
          </Button>
        </p>
      ) : null}
      {outage ? <p>{outageMessages[outage]}</p> : null}
      {notice ? <p>{notice}</p> : null}
    </div>
  )
}
