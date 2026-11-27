import { Button } from '@/components/ui/button'

export function FullPageStatus({
  title,
  detail,
  action,
}: {
  title: string
  detail?: string
  action?: { label: string; onClick: () => void }
}) {
  return (
    <main className="flex min-h-svh items-center justify-center bg-canvas p-6 text-ink">
      <div className="max-w-sm text-center">
        <p className="font-display text-2xl">{title}</p>
        {detail ? <p className="mt-2 text-sm text-ink-secondary">{detail}</p> : null}
        {action ? (
          <Button className="mt-4" onClick={action.onClick}>
            {action.label}
          </Button>
        ) : null}
      </div>
    </main>
  )
}
