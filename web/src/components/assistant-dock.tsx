import { useRouterState } from '@tanstack/react-router'
import { MessageCircle, Sparkles, X } from 'lucide-react'
import { useEffect, useRef } from 'react'
import { AssistantHome } from '@/components/assistant-home'
import type { AssistantArea } from '@/lib/assistant'
import { closeAssistant, isWide, openAssistant, useAssistantDock } from '@/lib/interface'
import { cn } from '@/lib/utils'

// Hidden, not unmounted, when closed: the conversation survives closing the panel and moving between pages.
export function AssistantDock({ area }: { area: AssistantArea }) {
  const dock = useAssistantDock()
  const pathname = useRouterState({ select: (s) => s.location.pathname })
  const shown = useRef(pathname)
  // On a phone the panel covers the page, so a page opened from it closes it.
  useEffect(() => {
    if (shown.current === pathname) return
    shown.current = pathname
    if (dock.open && !isWide()) closeAssistant()
  }, [pathname, dock.open])
  if (!dock.available) return null

  return (
    <>
      {dock.open ? null : (
        <button
          type="button"
          aria-label="Open the assistant"
          onClick={() => openAssistant()}
          className="fixed right-4 bottom-[calc(4.5rem+env(safe-area-inset-bottom))] z-30 grid size-14 place-items-center rounded-full bg-accent text-accent-ink shadow-pop transition-transform hover:scale-105 md:right-6 md:bottom-6"
        >
          <MessageCircle className="size-6" />
        </button>
      )}
      <aside
        data-testid="assistant-dock"
        aria-label="Assistant"
        hidden={!dock.open}
        className={cn(
          'fixed inset-0 z-40 flex-col border-line bg-canvas shadow-pop md:left-auto md:w-[28rem] md:border-l',
          dock.open ? 'flex' : 'hidden',
        )}
      >
        <header className="flex h-14 shrink-0 items-center gap-2 border-b border-line px-4">
          <Sparkles className="size-4 text-accent" aria-hidden />
          <h2 className="font-medium text-ink">Assistant</h2>
          <button
            type="button"
            aria-label="Close the assistant"
            onClick={closeAssistant}
            className="ml-auto grid size-11 place-items-center rounded-md text-ink-secondary hover:bg-surface-sunken hover:text-ink"
          >
            <X className="size-5" />
          </button>
        </header>
        <div className="min-h-0 flex-1 overflow-y-auto px-4 pt-4">
          <AssistantHome area={area} compact question={dock.question} />
        </div>
      </aside>
    </>
  )
}
