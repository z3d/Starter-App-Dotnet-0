import { createFileRoute } from '@tanstack/react-router'
import { Lock } from 'lucide-react'
import { PalettePicker } from '@/components/palette-picker'
import { Card, EmptyState, PageHeader } from '@/components/ui/primitives'
import { useCanAdminister } from '@/lib/appearance'

export const Route = createFileRoute('/_app/settings')({
  component: SettingsPage,
})

function SettingsPage() {
  const canAdminister = useCanAdminister()
  return (
    <>
      <PageHeader
        eyebrow="Administration"
        title="Settings"
        description="What everyone in your organisation sees. Light or dark is each person's own choice, under Appearance."
      />
      {canAdminister ? (
        <Card className="p-6">
          <h2 className="mb-1 font-display text-xl text-ink">Colour theme</h2>
          <p className="mb-4 text-ink-secondary">Every screen, for everyone, in light and dark.</p>
          <PalettePicker />
        </Card>
      ) : (
        <EmptyState
          icon={Lock}
          title="Only an administrator can change these"
          description="Ask an administrator in your organisation to change the colour theme."
        />
      )}
    </>
  )
}
