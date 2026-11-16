import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'

// Tailwind reads class names as whitespace-separated tokens, so an arbitrary value with a space in it
// (`bg-[url("...<svg width=...")]`) is split apart and silently generates nothing. The select's arrow was
// lost that way. Spaces in an arbitrary value are written as underscores, or the value becomes a token.
const arbitraryWithSpace = /(?<![\w-])[a-z][a-z0-9:-]*-\[[^\]'`\n]* [^\]\n]*\]/g

function sourceFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name)
    if (entry.isDirectory()) return sourceFiles(path)
    return /\.tsx?$/.test(entry.name) && !entry.name.includes('.generated.') && !entry.name.endsWith('.test.ts')
      ? [path]
      : []
  })
}

describe('Tailwind class names', () => {
  it('never hold a space inside an arbitrary value', () => {
    const offenders = sourceFiles('src').flatMap((file) =>
      [...readFileSync(file, 'utf8').matchAll(arbitraryWithSpace)].map((m) => `${file}: ${m[0].slice(0, 60)}`),
    )
    expect(offenders).toEqual([])
  })

  it('give a select its arrow in every theme', () => {
    const css = readFileSync('src/index.css', 'utf8')
    expect(css).toContain('@utility select-chevron')
    expect(css.match(/--chevron:/g)).toHaveLength(3) // light, system dark, chosen dark
    expect(readFileSync('src/components/ui/field.tsx', 'utf8')).toContain("'select-chevron'")
  })
})
