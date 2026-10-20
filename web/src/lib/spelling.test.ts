/// <reference types="node" />
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'
import { describe, expect, it } from 'vitest'

const americanSpellings: Array<[RegExp, string]> = [
  [/\binquir(y|ies|e|ed|es)\b/i, 'enquiry (a formal inquiry is an investigation, not a question)'],
  [/(?<![-\w])(center|centers)\b/i, 'centre'],
  [/\bneighbor(s|hood|hoods|ing)?\b/i, 'neighbour, neighbourhood'],
  [/\ba license\b|\bthe license\b|\blicense (number|holder|condition|class)s?\b/i, 'licence (the noun)'],
  [/\bbehavior(s|al)?\b/i, 'behaviour'],
  [/\bfavorite(s|d)?\b/i, 'favourite'],
  [/\bmold(s|y)?\b/i, 'mould'],
  [/\borganiz(e|ed|es|ing|ation|ations)\b/i, 'organise, organisation'],
  [/\b(un)?recogniz(e|ed|es|ing|able)\b/i, 'recognise'],
  [/\banalyz(e|ed|es|ing)\b/i, 'analyse'],
  [/\bapologiz(e|ed|es|ing)\b/i, 'apologise'],
  [
    /\bauthoriz(e|ed|es|ing|ation|ations)\b(?!\s*:)/i,
    'authorise, authorisation (the HTTP Authorization header is exempt)',
  ],
  [/\bcancel(ed|ing)\b/i, 'cancelled, cancelling'],
  [/\blabel(ed|ing)\b/i, 'labelled, labelling'],
  [/\bcatalog\b/i, 'catalogue'],
  [/\bdefense\b/i, 'defence'],
  [/\bgray\b/i, 'grey'],
]

// Every string is matched so quotes pair up; only one with a space in it is text a person reads.
const quoted = /'((?:[^'\\\n]|\\.)*)'|"((?:[^"\\\n]|\\.)*)"|`([^`]*)`|>([^<>{}()=;[\]\n]*[A-Za-z][^<>{}()=;[\]\n]*)</g

const wrappedJsxText = /^\s*[A-Z][^<>{}()=;[\]'"`]*\s[^<>{}()=;[\]'"`]*$/

const walkthrough = 'e2e/walkthrough.mjs'
const roots = ['src', walkthrough]

function files(path: string): string[] {
  if (statSync(path).isFile()) return [path]
  return readdirSync(path).flatMap((name) => {
    const full = join(path, name)
    if (statSync(full).isDirectory()) return name === 'node_modules' ? [] : files(full)
    return /\.(tsx?|mjs)$/.test(name) && !/\.test\./.test(name) && !/\.generated\./.test(name) ? [full] : []
  })
}

describe('Australian English', () => {
  it('is what every screen and the walkthrough say', () => {
    const found: string[] = []
    for (const file of roots.flatMap(files)) {
      const narrated = file === walkthrough
      readFileSync(file, 'utf8')
        .split('\n')
        .forEach((line, index) => {
          if (/^\s*import /.test(line) || (!narrated && /^\s*(\/\/|\*)/.test(line))) return
          const texts =
            narrated || wrappedJsxText.test(line)
              ? [line]
              : [...line.matchAll(quoted)].map((m) => m[4] ?? m[1] ?? m[2] ?? m[3] ?? '').filter((t) => /\s/.test(t))
          for (const text of texts)
            for (const [pattern, instead] of americanSpellings) {
              const hit = text.match(pattern)
              if (hit) found.push(`${relative(process.cwd(), file)}:${index + 1} "${hit[0]}" — write ${instead}`)
            }
        })
    }
    expect(found).toEqual([])
  })
})
