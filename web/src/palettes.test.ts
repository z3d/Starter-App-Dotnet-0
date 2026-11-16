/// <reference types="node" />
import { readFileSync } from 'node:fs'
import { modeKey, paletteKey } from '@/lib/appearance'
import { defaultPalette, palettes } from './palettes'

const index = readFileSync('src/index.css', 'utf8')
const themes = readFileSync('src/palettes.css', 'utf8')
const prePaint = readFileSync('public/appearance.js', 'utf8')

function tokens(css: string, selector: string) {
  const start = css.indexOf(`${selector} {`)
  if (start < 0) return undefined
  const body = css.slice(css.indexOf('{', start) + 1, css.indexOf('}', start))
  return Object.fromEntries([...body.matchAll(/--([\w-]+):\s*([^;]+);/g)].map((m) => [m[1]!, m[2]!.trim()]))
}

const blocks = (id: string) => [
  `:root[data-palette='${id}']`,
  `:root[data-palette='${id}']:not([data-theme='light'])`,
  `:root[data-palette='${id}'][data-theme='dark']`,
]
const colourTokens = Object.keys(tokens(index, ':root')!).sort()

describe('the colour themes', () => {
  it('declares the default among them, once each, under names the API accepts', () => {
    const ids = palettes.map((p) => p.id)
    expect(ids).toContain(defaultPalette)
    expect(new Set(ids).size).toBe(ids.length)
    for (const id of ids) expect(id).toMatch(/^[a-z][a-z0-9-]{0,31}$/)
  })

  it('keeps the default in index.css and every other theme in palettes.css, nothing undeclared', () => {
    const inCss = [...new Set([...themes.matchAll(/data-palette='([^']+)'/g)].map((m) => m[1]))].sort()
    const others = palettes.map((p) => p.id).filter((id) => id !== defaultPalette)
    expect(inCss).toEqual([...others].sort())
    expect(index).not.toContain('data-palette')
  })

  it.each(palettes.filter((p) => p.id !== defaultPalette).map((p) => p.id))(
    '%s redefines every colour token in light, system dark and chosen dark',
    (id) => {
      for (const selector of blocks(id))
        expect(Object.keys(tokens(themes, selector) ?? {}).sort()).toEqual(colourTokens)
    },
  )

  it.each(palettes.map((p) => [p.id, p.swatch] as const))('%s shows its own accent and highlight', (id, swatch) => {
    const light = { ...tokens(index, ':root')!, ...(id === defaultPalette ? {} : tokens(themes, blocks(id)[0]!)) }
    expect(swatch).toEqual([light.accent, light.highlight])
  })

  it('is read before the first paint from the keys the app writes', () => {
    expect(prePaint).toContain(`'${modeKey}'`)
    expect(prePaint).toContain(`'${paletteKey}'`)
    expect(readFileSync('index.html', 'utf8')).toMatch(
      /<head>[\s\S]*<script src="\/appearance\.js"><\/script>[\s\S]*<\/head>/,
    )
  })
})
