// @vitest-environment node
import { existsSync, readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, it } from 'vitest'

const path = (relative: string) => fileURLToPath(new URL(relative, import.meta.url))
const read = (relative: string) => readFileSync(path(relative), 'utf8')

// Sign-in runs through the API, so the image carries no identity provider and the page talks only to its own origin.
it('serves a page that knows no identity provider and connects only to its own origin', () => {
  expect(read('../../index.html')).not.toContain('config.js')
  expect(existsSync(path('../../deploy/config.js.template'))).toBe(false)
  expect(read('../../Dockerfile')).not.toMatch(/OIDC_|config\.js/)
  const policy = /Content-Security-Policy "([^"]+)"/.exec(read('../../deploy/security-headers.conf'))![1]!
  expect(policy).toContain("connect-src 'self';")
})

// A derived project replaces the sample's scopes in auth.ts; the session the API opens must ask for the same ones,
// or the provider refuses a scope it does not know and every sign-in fails.
it('has the API ask the identity provider for the scopes the app expects', () => {
  const app = /resourceScopes = \[([^\]]+)\]/
    .exec(read('../lib/auth.ts'))![1]!
    .replace(/['\s]/g, '')
    .split(',')
    .filter(Boolean)
  const appHost = /"Identity:Session:Scope"\]\s*\?\?\s*"([^"]+)"/.exec(
    read('../../../dev/StarterApp.AppHost/Program.cs'),
  )![1]!
  expect(appHost).toBe(`openid ${app.join(' ')}`)
})
