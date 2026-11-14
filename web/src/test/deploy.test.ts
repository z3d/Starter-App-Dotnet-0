// @vitest-environment node
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, it } from 'vitest'

const read = (path: string) => readFileSync(fileURLToPath(new URL(path, import.meta.url)), 'utf8')

// The identity helper reads runtime.<key>; the template renders each one; the entrypoint exports each variable.
it('renders every runtime value the identity helper reads', () => {
  const readByAuth = [...read('../lib/auth.ts').matchAll(/runtime\.(\w+)/g)].map((m) => m[1]!)
  const template = read('../../deploy/config.js.template')
  const rendered = [...template.matchAll(/(\w+): "\$\{(\w+)\}"/g)].map((m) => ({ key: m[1]!, variable: m[2]! }))
  const exported = /envsubst '([^']+)'/.exec(read('../../deploy/10-render-config.sh'))?.[1] ?? ''

  expect(new Set(rendered.map((r) => r.key))).toEqual(new Set(readByAuth))
  for (const { variable } of rendered) expect(exported).toContain(`\${${variable}}`)
})

// The image's default scope is what the bundle asks for. A derived project replaces the sample's scopes in auth.ts,
// and an image still naming the old ones fails every hosted sign-in, because the identity provider refuses a scope it does not know.
it('asks the identity provider for the same scopes hosted as in development', () => {
  const dev = /resourceScopes = \[([^\]]+)\]/
    .exec(read('../lib/auth.ts'))![1]!
    .replace(/['\s]/g, '')
    .split(',')
    .filter(Boolean)
  const hosted = /OIDC_SCOPE:=([^}]+)\}/.exec(read('../../deploy/10-render-config.sh'))![1]!
  expect(hosted).toBe(`openid ${dev.join(' ')}`)
})
