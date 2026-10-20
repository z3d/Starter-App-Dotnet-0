import { Chip } from '@/components/ui/primitives'
import { statusTone, words } from '@/lib/format'

export function StatusChip({ status }: { status: string | null | undefined }) {
  return (
    <Chip tone={statusTone(status)} data-testid="status">
      {words(status)}
    </Chip>
  )
}
