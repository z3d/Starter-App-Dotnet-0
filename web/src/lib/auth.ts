import { UserManager, WebStorageStateStore, type UserManagerSettings } from 'oidc-client-ts'

// The sample's resource scopes; a derived project replaces them with its modules' scopes.
export const resourceScopes = [
  'products:read',
  'products:write',
  'customers:read',
  'customers:write',
  'orders:read',
  'orders:write',
] as const
export const DEFAULT_SCOPES = `openid ${resourceScopes.join(' ')}`

// A hosted instance renders /config.js at start-up (deploy/config.js.template) with the identity
// provider's realm, so it is a deploy-time value, never baked into the bundle. The audience and
// the organization are what an Auth0 tenant needs; a Keycloak realm needs neither.
type RuntimeConfig = {
  oidcAuthority?: string
  oidcClientId?: string
  oidcScope?: string
  oidcAudience?: string
  oidcOrganization?: string
}
const runtime = (window as unknown as { __STARTERAPP_CONFIG__?: RuntimeConfig }).__STARTERAPP_CONFIG__ ?? {}
const present = (value: string | undefined): string | undefined =>
  value && !value.startsWith('${') && !/replace-me/i.test(value) ? value : undefined

const audience = present(runtime.oidcAudience) ?? present(import.meta.env.VITE_OIDC_AUDIENCE as string | undefined)
const organization =
  present(runtime.oidcOrganization) ?? present(import.meta.env.VITE_OIDC_ORGANIZATION as string | undefined)
const extraQueryParams = { ...(audience ? { audience } : {}), ...(organization ? { organization } : {}) }

// OIDC settings for the SPA. Authorization code + PKCE against whichever provider the AppHost (or
// deployment) points us at; the dev default is the AppHost's Keycloak "starterapp" realm and its
// public `starterapp-web` client. Tokens are held in sessionStorage (per-tab, cleared on close):
// the API validates the bearer JWT itself, so nothing here is trusted by the server.
export const oidcSettings: UserManagerSettings = {
  authority:
    present(runtime.oidcAuthority) ??
    present(import.meta.env.VITE_OIDC_AUTHORITY as string | undefined) ??
    'http://localhost:8090/realms/starterapp',
  client_id: present(runtime.oidcClientId) ?? import.meta.env.VITE_OIDC_CLIENT_ID ?? 'starterapp-web',
  redirect_uri: `${window.location.origin}/auth/callback`,
  post_logout_redirect_uri: window.location.origin,
  silent_redirect_uri: `${window.location.origin}/auth/silent`,
  response_type: 'code',
  // Keycloak rejects a scope it does not know, so the default names the API scopes the app uses.
  scope: present(runtime.oidcScope) ?? import.meta.env.VITE_OIDC_SCOPES ?? DEFAULT_SCOPES,
  automaticSilentRenew: true,
  loadUserInfo: false,
  userStore: new WebStorageStateStore({ store: window.sessionStorage }),
  ...(Object.keys(extraQueryParams).length > 0 ? { extraQueryParams } : {}),
}

// After the provider bounces back to /auth/callback, return the user to where they started.
export function onSigninCallback() {
  const returnTo = window.sessionStorage.getItem('starterapp.returnTo') ?? '/'
  window.sessionStorage.removeItem('starterapp.returnTo')
  window.history.replaceState({}, document.title, returnTo)
}

export function rememberReturnTo() {
  window.sessionStorage.setItem('starterapp.returnTo', window.location.pathname + window.location.search)
}

// A second manager over the same settings and store, for the API client to send the person back
// to the provider: the AuthProvider's own manager reads the same sessionStorage on the callback.
let manager: UserManager | undefined
function userManager() {
  manager ??= new UserManager(oidcSettings)
  return manager
}

// The claims the API verifies are the access token's; the id token is for the app and may carry fewer.
export function claimsOf(accessToken: string | undefined): Record<string, unknown> {
  const payload = accessToken?.split('.')[1]
  if (!payload) return {}
  return JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/'))) as Record<string, unknown>
}

// The session is gone (a 401): sign in again, back to the same page.
export function signInAgain(): void {
  rememberReturnTo()
  void userManager().signinRedirect()
}

// Sign in again asking for exactly these scopes, so the API's answer shows what the token decides.
export function signInWithScopes(scopes: readonly string[]): void {
  rememberReturnTo()
  void userManager().signinRedirect({ scope: `openid ${scopes.join(' ')}`.trim() })
}

// The API's step-up challenge (RFC 9470, a 403 with insufficient_user_authentication) names the
// authentication level it wants; sign in again at that level rather than showing a dead end.
export function stepUp(challenge: string | null): boolean {
  if (!challenge || !challenge.includes('insufficient_user_authentication')) return false
  const acr = /acr_values="([^"]+)"/.exec(challenge)?.[1]
  rememberReturnTo()
  void userManager().signinRedirect(acr ? { acr_values: acr } : undefined)
  return true
}
