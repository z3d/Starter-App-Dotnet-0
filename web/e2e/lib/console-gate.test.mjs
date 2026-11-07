// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { refusal } from './console-gate.mjs'

const post = (headers) => refusal({ method: 'POST', headers: { host: 'localhost:4173', ...headers } }, 4173)

describe('the presenter console gate', () => {
  it('takes a POST from its own page', () => {
    expect(post({ origin: 'http://localhost:4173', 'sec-fetch-site': 'same-origin' })).toBeNull()
    expect(post({ host: '127.0.0.1:4173', origin: 'http://127.0.0.1:4173' })).toBeNull()
  })

  it('takes a POST from a non-browser client such as curl', () => {
    expect(post({})).toBeNull()
  })

  it('refuses a POST from any other page the browser has open', () => {
    expect(post({ origin: 'https://example.com' })).toBe('foreign origin')
    expect(post({ origin: 'http://localhost:5173' })).toBe('foreign origin')
    expect(post({ origin: 'null' })).toBe('foreign origin')
    expect(post({ 'sec-fetch-site': 'cross-site' })).toBe('cross-site request')
    expect(post({ 'sec-fetch-site': 'same-site' })).toBe('cross-site request')
    expect(post({ referer: 'https://example.com/page' })).toBe('foreign referer')
    expect(post({ referer: 'http://localhost:41730/' })).toBe('foreign referer')
  })

  it('refuses any request addressed to another host name, as a rebound DNS name would be', () => {
    expect(refusal({ method: 'GET', headers: { host: 'attacker.example:4173' } }, 4173)).toBe('unknown host')
    expect(post({ host: 'attacker.example:4173', origin: 'http://attacker.example:4173' })).toBe('unknown host')
    expect(refusal({ method: 'GET', headers: {} }, 4173)).toBe('unknown host')
  })

  it('serves its own page and stream', () => {
    expect(refusal({ method: 'GET', headers: { host: 'localhost:4173' } }, 4173)).toBeNull()
  })
})
