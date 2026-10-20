import type { ReactNode } from 'react'
import { Dialog as RadixDialog } from 'radix-ui'

export function Dialog({
  open,
  onOpenChange,
  title,
  description,
  children,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: ReactNode
  description?: ReactNode
  children: ReactNode
}) {
  return (
    <RadixDialog.Root open={open} onOpenChange={onOpenChange}>
      <RadixDialog.Portal>
        <RadixDialog.Overlay className="fixed inset-0 z-50 bg-ink/30 backdrop-blur-[2px]" />
        <RadixDialog.Content className="fixed top-[10%] left-1/2 z-50 max-h-[80vh] w-[min(36rem,calc(100vw-2rem))] -translate-x-1/2 overflow-y-auto rounded-xl bg-surface-raised p-5 text-ink shadow-pop outline-none">
          <RadixDialog.Title className="font-display text-2xl leading-tight">{title}</RadixDialog.Title>
          {description ? (
            <RadixDialog.Description className="mt-1 text-sm text-ink-secondary">{description}</RadixDialog.Description>
          ) : (
            <RadixDialog.Description className="sr-only">{title}</RadixDialog.Description>
          )}
          <div className="mt-4">{children}</div>
        </RadixDialog.Content>
      </RadixDialog.Portal>
    </RadixDialog.Root>
  )
}
