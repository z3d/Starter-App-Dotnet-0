/// <reference types="node" />
import { render } from '@testing-library/react'
import { readFileSync } from 'node:fs'
import type { ComponentProps, ReactElement } from 'react'
import { Button } from './button'
import { Figure } from './figures'
import { Avatar, Callout, Chip, StatTile } from './primitives'
import { Waited } from './queue'

const css = readFileSync('src/index.css', 'utf8')

function declarations(selector: string) {
  const start = css.indexOf(`${selector} {`)
  if (start < 0) throw new Error(`no ${selector} block in index.css`)
  const body = css.slice(css.indexOf('{', start) + 1, css.indexOf('}', start))
  return Object.fromEntries(
    [...body.matchAll(/--([\w-]+):\s*([^;]+);/g)].map((m) => [
      m[1]!,
      m[2]!.replace(/\s+/g, ' ').replace(/\( /g, '(').replace(/ \)/g, ')').trim(),
    ]),
  )
}

const light = declarations(':root')
const themes = {
  light,
  'dark (system)': { ...light, ...declarations(":root:not([data-theme='light'])") },
  'dark (chosen)': { ...light, ...declarations(":root[data-theme='dark']") },
}
const tailwind = declarations('@theme inline')

type Oklch = { l: number; c: number; h: number }

function channel(expression: string, base: Oklch | undefined): number {
  const named = (token: string) => {
    if (token in (base ?? {})) return base![token as keyof Oklch]
    const value = Number(token)
    if (Number.isNaN(value)) throw new Error(`cannot read ${token}`)
    return value
  }
  const calc = /^calc\((\S+) ([*+-]) (\S+)\)$/.exec(expression)
  if (!calc) return named(expression)
  const [a, b] = [named(calc[1]!), named(calc[3]!)]
  return calc[2] === '*' ? a * b : calc[2] === '+' ? a + b : a - b
}

function oklch(value: string, vars: Record<string, string>): Oklch {
  const alias = /^var\(--([\w-]+)\)$/.exec(value)
  if (alias) return token(alias[1]!, vars)
  const relative = /^oklch\(from var\(--([\w-]+)\) (.+)\)$/.exec(value)
  if (relative) {
    const base = token(relative[1]!, vars)
    const [l, c, h, ...rest] = relative[2]!.match(/calc\([^)]*\)|\S+/g) ?? []
    if (!h || rest.length) throw new Error(`not an opaque oklch colour: ${value}`)
    return { l: channel(l!, base), c: channel(c!, base), h: channel(h, base) }
  }
  const absolute = /^oklch\(([\d.]+) ([\d.]+) ([\d.]+)\)$/.exec(value)
  if (!absolute) throw new Error(`not an opaque oklch colour: ${value}`)
  return { l: Number(absolute[1]), c: Number(absolute[2]), h: Number(absolute[3]) }
}

function token(name: string, vars: Record<string, string>): Oklch {
  const value = vars[name]
  if (!value) throw new Error(`--${name} is not set`)
  return oklch(value, vars)
}

function luminance({ l: L, c: C, h: H }: Oklch) {
  const a = C * Math.cos((H * Math.PI) / 180)
  const b = C * Math.sin((H * Math.PI) / 180)
  const l = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3
  const m = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3
  const s = (L - 0.0894841775 * a - 1.291485548 * b) ** 3
  const clip = (x: number) => Math.min(1, Math.max(0, x))
  const r = clip(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s)
  const g = clip(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s)
  const bl = clip(-0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s)
  return 0.2126 * r + 0.7152 * g + 0.0722 * bl
}

function contrast(x: Oklch, y: Oklch) {
  const [hi, lo] = [luminance(x), luminance(y)].sort((p, q) => q - p)
  return (hi! + 0.05) / (lo! + 0.05)
}

const chipTones = {
  neutral: true,
  positive: true,
  caution: true,
  negative: true,
  info: true,
  ai: true,
  accent: true,
} satisfies Record<NonNullable<ComponentProps<typeof Chip>['tone']>, true>
const calloutTones = { info: true, caution: true, negative: true } satisfies Record<
  NonNullable<ComponentProps<typeof Callout>['tone']>,
  true
>

const waitTones = { fresh: true, slow: true, late: true } satisfies Record<
  NonNullable<ComponentProps<typeof Waited>['tone']>,
  true
>
const tileTones = { accent: true, positive: true, caution: true } satisfies Record<
  NonNullable<ComponentProps<typeof StatTile>['tone']>,
  true
>
const figureTones = { neutral: true, accent: true, positive: true, caution: true } satisfies Record<
  NonNullable<ComponentProps<typeof Figure>['tone']>,
  true
>

function colourClasses(element: Element, onCard = false) {
  const pick = (prefix: string) =>
    [...element.classList]
      .filter((c) => c.startsWith(prefix) && `color-${c.slice(prefix.length)}` in tailwind)
      .map((c) => c.slice(prefix.length))
  const [text, surface] = [pick('text-'), onCard ? ['surface'] : pick('bg-')]
  if (text.length !== 1 || surface.length !== 1) throw new Error(`no single text and surface on ${element.className}`)
  return { text: text[0]!, surface: surface[0]! }
}

function pair(name: string, ui: ReactElement, find: (root: HTMLElement) => Element, onCard = false) {
  const { container, unmount } = render(ui)
  const classes = colourClasses(find(container), onCard)
  unmount()
  return { name, ...classes }
}

const pairs = [
  ...Object.keys(chipTones).map((tone) =>
    pair(`chip ${tone}`, <Chip tone={tone as keyof typeof chipTones}>x</Chip>, (root) => root.firstElementChild!),
  ),
  ...Object.keys(calloutTones).map((tone) =>
    pair(
      `callout ${tone}`,
      <Callout tone={tone as keyof typeof calloutTones}>x</Callout>,
      (root) => root.firstElementChild!,
    ),
  ),
  ...['a', 'b', 'c', 'd', 'e'].map((name) =>
    pair(`avatar ${name}`, <Avatar name={name} />, (root) => root.firstElementChild!),
  ),
  pair('danger button', <Button variant="danger">x</Button>, (root) => root.firstElementChild!),
  ...Object.keys(waitTones).map((tone) =>
    pair(
      `waited ${tone}`,
      <Waited days={3} tone={tone as keyof typeof waitTones} />,
      (root) => root.firstElementChild!,
      true,
    ),
  ),
  ...Object.keys(tileTones).map((tone) =>
    pair(
      `stat tile ${tone}`,
      <StatTile label="l" value="v" tone={tone as keyof typeof tileTones} />,
      (root) => root.querySelector('.font-display')!,
      true,
    ),
  ),
  ...Object.keys(figureTones).map((tone) =>
    pair(
      `figure ${tone}`,
      <Figure label="l" value="v" tone={tone as keyof typeof figureTones} />,
      (root) => root.querySelector('.font-display')!,
      true,
    ),
  ),
]

const cases = pairs.flatMap((p) =>
  Object.entries(themes).map(([theme, vars]) => {
    const resolve = (utility: string) => oklch(tailwind[`color-${utility}`]!, vars)
    const ratio = contrast(resolve(p.text), resolve(p.surface))
    return { ...p, theme, ratio, shown: ratio.toFixed(2) }
  }),
)

describe('tinted text on its surface', () => {
  it('measures sRGB the way WCAG does', () => {
    expect(contrast({ l: 1, c: 0, h: 0 }, { l: 0, c: 0, h: 0 })).toBeCloseTo(21, 2)
    expect(contrast({ l: 0.627955, c: 0.257683, h: 29.2339 }, { l: 1, c: 0, h: 0 })).toBeCloseTo(4, 2)
    expect(contrast({ l: Math.cbrt(0.181164), c: 0, h: 0 }, { l: 1, c: 0, h: 0 })).toBeCloseTo(4.54, 2)
  })

  it('covers every avatar hue', () => {
    expect(new Set(pairs.filter((p) => p.name.startsWith('avatar')).map((p) => p.text)).size).toBe(5)
  })

  it.each(cases)('$name in $theme: $text on $surface is $shown:1', ({ ratio }) => {
    expect(ratio).toBeGreaterThanOrEqual(4.5)
  })
})
