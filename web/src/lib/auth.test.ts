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
  it('asks the API to end the session with the CSRF token and the reason, then goes where it says', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(Response.json({ location: 'https://idp.test/logout?id_token_hint=x' }))
    auth.setCsrfToken('csrf-abc')
    await auth.signOut('idle')
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/auth/logout')
    expect((init.headers as Record<string, string>)['X-CSRF-Token']).toBe('csrf-abc')
    expect((init.headers as Record<string, string>).Accept).toBe('application/json')
    expect(new URLSearchParams(String(init.body)).get('reason')).toBe('idle')
    expect(go).toHaveBeenCalledWith('https://idp.test/logout?id_token_hint=x')
  })

  it('still leaves when the API cannot be reached', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new Error('offline'))
    await auth.signOut()
    expect(go).toHaveBeenCalledWith('/signed-out?reason=signed-out')
  })
})

describe('the token', () => {
  it('is never kept where a script on the page could read it', () => {
    auth.setCsrfToken('csrf-abc')
    expect(Object.keys(window.sessionStorage)).toEqual([])
    expect(Object.keys(window.localStorage).filter((key) => /oidc|token/i.test(key))).toEqual([])
  })
})
