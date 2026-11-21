import { AssistantTable } from '@/components/assistant-table'
import type { AssistantGraphic as Graphic } from '@/lib/assistant'
import { readSvg } from '@/lib/graphic'

// A graph the assistant drew in the app's palette, with the figures it was drawn from underneath, since the drawing is the assistant's own.
export function AssistantGraphic({ graphic }: { graphic: Graphic }) {
  return (
    <figure data-testid="assistant-graphic" aria-label={graphic.title} className="space-y-2">
      <figcaption className="text-2xs font-medium tracking-[0.08em] text-ink-tertiary uppercase">
        {graphic.title}
      </figcaption>
      <div className="rounded-lg border border-line bg-surface p-3 text-ink">{readSvg(graphic.svg)}</div>
      {graphic.note ? <p className="text-sm text-ink-secondary">{graphic.note}</p> : null}
      <details className="text-sm">
        <summary className="cursor-pointer text-ink-secondary">The figures behind this graph</summary>
        <div className="mt-2">
          <AssistantTable table={graphic.data} />
        </div>
      </details>
    </figure>
  )
}
