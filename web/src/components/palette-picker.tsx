import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Check } from 'lucide-react'
import { useState } from 'react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Callout } from '@/components/ui/primitives'
import { call, client } from '@/lib/api'
import { appearanceQueryKey, applyPalette, isPalette, useTenantAppearance } from '@/lib/appearance'
import { whenAt } from '@/lib/format'
import { cn } from '@/lib/utils'
import { defaultPalette, type PaletteId, palettes } from '@/palettes'

export function PalettePicker() {
  const appearance = useTenantAppearance()
  const queryClient = useQueryClient()
  const saved: PaletteId = isPalette(appearance.data?.palette) ? appearance.data.palette : defaultPalette
  const [picked, setChosen] = useState<PaletteId | null>(null)
  const chosen = picked ?? saved

  const save = useMutation({
    mutationFn: (palette: PaletteId) => call(client.PUT('/api/v1/settings/appearance', { body: { palette } })),
    onSuccess: (result) => {
      applyPalette(result.palette)
      queryClient.setQueryData(appearanceQueryKey, result)
      setChosen(null)
      toast.success('Colour theme saved for everyone')
    },
  })

  return (
    <div className="space-y-4">
      <div role="radiogroup" aria-label="Colour theme" className="grid gap-3 sm:grid-cols-2">
        {palettes.map((palette) => {
          const selected = palette.id === chosen
          return (
            <button
              key={palette.id}
              type="button"
              role="radio"
              aria-checked={selected}
              onClick={() => setChosen(palette.id)}
              className={cn(
                'flex items-start gap-3 rounded-lg bg-surface p-3 text-left card transition-shadow',
                selected ? 'ring-2 ring-accent' : 'hover:shadow-lift',
              )}
            >
              <span className="mt-0.5 flex shrink-0 -space-x-1.5" aria-hidden>
                {palette.swatch.map((colour) => (
                  <span
                    key={colour}
                    className="size-5 rounded-full ring-2 ring-surface"
                    style={{ background: colour }}
                  />
                ))}
              </span>
              <span className="min-w-0 flex-1">
                <span className="flex items-center gap-1.5 font-medium text-ink">
                  {palette.label}
                  {palette.id === saved ? <span className="text-2xs font-normal text-ink-tertiary">in use</span> : null}
                </span>
                <span className="block text-xs text-ink-secondary">{palette.description}</span>
              </span>
              {selected ? <Check className="size-4 shrink-0 text-accent-strong" aria-hidden /> : null}
            </button>
          )
        })}
      </div>
      {save.error ? <Callout tone="negative">{save.error.message}</Callout> : null}
      <div className="flex flex-wrap items-center gap-3">
        <Button variant="accent" disabled={chosen === saved || save.isPending} onClick={() => save.mutate(chosen)}>
          Save for everyone
        </Button>
        {appearance.data?.updatedBy && appearance.data.lastUpdated ? (
          <p className="text-xs text-ink-tertiary" data-testid="appearance-changed">
            Last changed by {appearance.data.updatedBy}, {whenAt(appearance.data.lastUpdated)}
          </p>
        ) : null}
      </div>
    </div>
  )
}
