import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const auth = await import('./auth')

let go: ReturnType<typeof vi.spyOn>
beforeEach(() => {
  window.sessionStorage.clear()
  go = vi.spyOn(auth.browser, 'go').mockImplementation(() => {})
})
afterEach(() => {
  vi.restoreAllMocks()
  auth.setCsrfToken(null)
})

const went = () => new URL(String(go.mock.calls.at(-1)?.[0]), 'https://app.test')

describe('signIn', () => {
  it('goes to the API to sign in, naming the page to come back to', () => {
    window.history.replaceState({}, '', '/orders/abc?tab=items')
    auth.signIn()
    expect(went().pathname).toBe('/api/auth/login')
    expect(went().searchParams.get('returnTo')).toBe('/orders/abc?tab=items')
  })
})

describe('stepUp', () => {
  it('does nothing for an ordinary refusal', () => {
    expect(auth.stepUp(null)).toBe(false)
    expect(auth.stepUp('Bearer error="insufficient_scope", scope="orders:write"')).toBe(false)
    expect(go).not.toHaveBeenCalled()
  })

  it('signs in again at the level the API asked for, back to the same page', () => {
    window.history.replaceState({}, '', '/products/new')
    expect(auth.stepUp('Bearer error="insufficient_user_authentication", acr_values="urn:starterapp:loa:mfa"')).toBe(
      true,
    )
    expect(went().searchParams.get('acr_values')).toBe('urn:starterapp:loa:mfa')
    expect(went().searchParams.get('returnTo')).toBe('/products/new')
  })

  it('still signs in again when the challenge names no level', () => {
    expect(auth.stepUp('Bearer error="insufficient_user_authentication"')).toBe(true)
    expect(went().searchParams.has('acr_values')).toBe(false)
  })
})

describe('signInAgain', () => {
  it('does not go a second time straight after the first, so a refused session cannot loop', () => {
    auth.signInAgain()
    auth.signInAgain()
    expect(go).toHaveBeenCalledTimes(1)
  })
})

describe('signInWithScopes', () => {
  it('asks for exactly those scopes', () => {
    window.history.replaceState({}, '', '/')
    auth.signInWithScopes(['products:read', 'customers:read'])
    expect(went().searchParams.get('scope')).toBe('openid products:read customers:read')
  })
})

describe('signOut', () => {
  it('posts a form to the API with the CSRF token and the reason, so the API can end the session at the provider', () => {
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})
    auth.setCsrfToken('csrf-abc')
    auth.signOut('idle')
    const form = submit.mock.contexts[0] as HTMLFormElement
    expect(form.method).toBe('post')
    expect(new URL(form.action).pathname).toBe('/api/auth/logout')
    expect(new FormData(form).get('csrf')).toBe('csrf-abc')
    expect(new FormData(form).get('reason')).toBe('idle')
  })
})

describe('the token', () => {
  it('is never kept where a script on the page could read it', () => {
    auth.setCsrfToken('csrf-abc')
    expect(Object.keys(window.sessionStorage)).toEqual([])
    expect(Object.keys(window.localStorage).filter((key) => /oidc|token/i.test(key))).toEqual([])
  })
})
