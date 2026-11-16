// The default's colours are index.css's own token blocks, the others' are in palettes.css; palettes.test.ts holds them in step.
export const palettes = [
  {
    id: 'teal',
    label: 'Teal',
    description: 'Teal and gold on warm paper, with a dark rail.',
    swatch: ['oklch(0.52 0.1 200)', 'oklch(0.76 0.12 85)'],
  },
  {
    id: 'sky',
    label: 'Sky',
    description: 'Clear blue on white, with a pale rail.',
    swatch: ['oklch(0.54 0.134 248)', 'oklch(0.53 0.1 195)'],
  },
  {
    id: 'fresh',
    label: 'Fresh',
    description: 'Green and mint on white, with a pale rail.',
    swatch: ['oklch(0.548 0.112 164)', 'oklch(0.53 0.13 245)'],
  },
  {
    id: 'coral',
    label: 'Coral',
    description: 'Light coral on white, with a pale rail.',
    swatch: ['oklch(0.686 0.169 29)', 'oklch(0.5 0.09 195)'],
  },
] as const

export type PaletteId = (typeof palettes)[number]['id']

export const defaultPalette: PaletteId = 'teal'
