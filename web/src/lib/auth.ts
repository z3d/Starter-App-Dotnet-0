// The browser holds no token: the API signs the person in, keeps the tokens and gives this tab a cookie it cannot read.
// What the tab keeps is the session's CSRF token, in memory only, which every change sends back.

// The sample's resource scopes; a derived project replaces them with its modules' scopes.
export const resourceScopes = [
  'products:read',
  'products:write',
  'customers:read',
  'customers:write',
  'orders:read',
  'orders:write',
  'settings:read',
  'settings:write',
  'assistant:use',
] as const

export const csrfHeader = 'X-CSRF-Token'
export const sessionEndedHeader = 'X-Session-Ended'
export type SignOutReason = 'signed-out' | 'idle' | 'expired'

let csrf: string | undefined

export function csrfToken(): string | undefined {
  return csrf
}

export function setCsrfToken(token: string | null | undefined): void {
  csrf = token ?? undefined
}

// A seam so tests can watch the navigation; jsdom cannot replace window.location.assign.
export const browser = {
  go: (url: string) => window.location.assign(url),
}

function here(): string {
  return window.location.pathname + window.location.search
}

export function signIn(returnTo: string = here(), extra: Record<string, string> = {}): void {
  const query = new URLSearchParams({ returnTo, ...extra })
  browser.go(`/api/auth/login?${query.toString()}`)
}

// The session is gone (a 401): sign in again, back to the same page.
export function signInAgain(): void {
  // A session the API keeps refusing (wrong audience, clock skew) would otherwise loop through the provider's live session forever.
  const last = Number(window.sessionStorage.getItem('starterapp.signInAgainAt'))
  if (last && Date.now() - last < 30_000) return
  window.sessionStorage.setItem('starterapp.signInAgainAt', String(Date.now()))
  signIn()
}

// Sign in again asking for exactly these scopes, so the API's answer shows what the token decides.
export function signInWithScopes(scopes: readonly string[]): void {
  signIn(here(), { scope: `openid ${scopes.join(' ')}`.trim() })
}

// The API's step-up challenge (RFC 9470, a 403 with insufficient_user_authentication) names the
// authentication level it wants; sign in again at that level rather than showing a dead end.
export function stepUp(challenge: string | null): boolean {
  if (!challenge || !challenge.includes('insufficient_user_authentication')) return false
  const acr = /acr_values="([^"]+)"/.exec(challenge)?.[1]
  signIn(here(), acr ? { acr_values: acr } : {})
  return true
}

// A form post, not a fetch: the API ends the session, then sends the browser to the provider to end it there too.
export function signOut(reason: SignOutReason = 'signed-out'): void {
  const form = document.createElement('form')
  form.method = 'POST'
  form.action = '/api/auth/logout'
  for (const [name, value] of Object.entries({ csrf: csrf ?? '', reason })) {
    const input = document.createElement('input')
    input.type = 'hidden'
    input.name = name
    input.value = value
    form.appendChild(input)
  }
  document.body.appendChild(form)
  form.submit()
}

export function isSignOutReason(value: string | null): value is SignOutReason {
  return value === 'signed-out' || value === 'idle' || value === 'expired'
}
