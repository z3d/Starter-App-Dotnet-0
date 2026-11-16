import { Check, Monitor, Moon, Sun, SunMoon } from 'lucide-react'
import { DropdownMenu } from 'radix-ui'
import { useState } from 'react'
import { applyMode, type Mode, storedMode } from '@/lib/appearance'
import { cn } from '@/lib/utils'

const modes: { value: Mode; label: string; icon: typeof Sun }[] = [
  { value: 'system', label: 'System', icon: Monitor },
  { value: 'light', label: 'Light', icon: Sun },
  { value: 'dark', label: 'Dark', icon: Moon },
]

export function AppearanceMenu({ className }: { className?: string }) {
  const [mode, setMode] = useState<Mode>(storedMode)

  const choose = (value: string) => {
    const next = value as Mode
    applyMode(next)
    setMode(next)
  }

  return (
    <DropdownMenu.Root>
      <DropdownMenu.Trigger aria-label="Appearance" className={className}>
        <SunMoon className="size-4" />
      </DropdownMenu.Trigger>
      <DropdownMenu.Portal>
        <DropdownMenu.Content
          side="top"
          align="end"
          sideOffset={8}
          className="z-50 min-w-44 rounded-lg bg-surface-raised p-1 text-sm text-ink shadow-pop"
        >
          <DropdownMenu.Label className="px-2 pt-1.5 pb-1 text-2xs font-medium tracking-[0.08em] text-ink-tertiary uppercase">
            Appearance
          </DropdownMenu.Label>
          <DropdownMenu.RadioGroup value={mode} onValueChange={choose}>
            {modes.map(({ value, label, icon: Icon }) => (
              <DropdownMenu.RadioItem
                key={value}
                value={value}
                className={cn(
                  'flex h-9 cursor-pointer items-center gap-2.5 rounded-md px-2 outline-none select-none',
                  'data-[highlighted]:bg-surface-sunken',
                )}
              >
                <Icon className="size-4 text-ink-tertiary" aria-hidden />
                <span className="flex-1">{label}</span>
                <DropdownMenu.ItemIndicator>
                  <Check className="size-4 text-accent-strong" aria-hidden />
                </DropdownMenu.ItemIndicator>
              </DropdownMenu.RadioItem>
            ))}
          </DropdownMenu.RadioGroup>
        </DropdownMenu.Content>
      </DropdownMenu.Portal>
    </DropdownMenu.Root>
  )
}
