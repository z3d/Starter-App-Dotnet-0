import { beforeEach, describe, expect, it, vi } from 'vitest'

// The identity helpers' own behaviour, with the OpenID Connect library stood in for.
const signinRedirect = vi.fn(async () => {})
vi.mock('oidc-client-ts', () => ({
  UserManager: class {
    signinRedirect = signinRedirect
  },
  WebStorageStateStore: class {},
  User: class {},
}))

const auth = await import('./auth')

describe('the return path', () => {
  beforeEach(() => {
    window.sessionStorage.clear()
    signinRedirect.mockClear()
  })

  it('remembers where the person was and takes them back after the callback', () => {
    window.history.replaceState({}, '', '/orders/abc?tab=items')
    auth.rememberReturnTo()
    expect(window.sessionStorage.getItem('starterapp.returnTo')).toBe('/orders/abc?tab=items')
    window.history.replaceState({}, '', '/auth/callback?code=x&state=y')
    auth.onSigninCallback()
    expect(window.location.pathname + window.location.search).toBe('/orders/abc?tab=items')
    expect(window.sessionStorage.getItem('starterapp.returnTo')).toBeNull()
  })

  it('falls back to the front page when nothing was remembered', () => {
    window.history.replaceState({}, '', '/auth/callback?code=x')
    auth.onSigninCallback()
    expect(window.location.pathname).toBe('/')
  })
})

describe('stepUp', () => {
  beforeEach(() => signinRedirect.mockClear())

  it('does nothing for an ordinary refusal', () => {
    expect(auth.stepUp(null)).toBe(false)
    expect(auth.stepUp('Bearer error="insufficient_scope", scope="orders:write"')).toBe(false)
    expect(signinRedirect).not.toHaveBeenCalled()
  })

  it('sends the person back to sign in at the level the API asked for, and remembers where they were', () => {
    window.history.replaceState({}, '', '/products/new')
    expect(auth.stepUp('Bearer error="insufficient_user_authentication", acr_values="urn:starterapp:loa:mfa"')).toBe(
      true,
    )
    expect(signinRedirect).toHaveBeenCalledWith({ acr_values: 'urn:starterapp:loa:mfa' })
    expect(window.sessionStorage.getItem('starterapp.returnTo')).toBe('/products/new')
  })

  it('still signs in again when the challenge names no level', () => {
    expect(auth.stepUp('Bearer error="insufficient_user_authentication"')).toBe(true)
    expect(signinRedirect).toHaveBeenCalledWith(undefined)
  })
})

describe('signInAgain', () => {
  it('starts the redirect and remembers the page', () => {
    window.history.replaceState({}, '', '/customers')
    auth.signInAgain()
    expect(signinRedirect).toHaveBeenCalled()
    expect(window.sessionStorage.getItem('starterapp.returnTo')).toBe('/customers')
  })
})

describe('signInWithScopes', () => {
  it('asks the provider for exactly those scopes and remembers the page', async () => {
    const auth = await import('./auth')
    window.history.replaceState({}, '', '/')
    auth.signInWithScopes(['products:read', 'customers:read'])
    expect(signinRedirect).toHaveBeenCalledWith({ scope: 'openid products:read customers:read' })
    expect(window.sessionStorage.getItem('starterapp.returnTo')).toBe('/')
  })
})
