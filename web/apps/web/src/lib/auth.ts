import { User, UserManager, WebStorageStateStore } from "oidc-client-ts";

// The audience and the organization are what an Auth0 tenant needs; a Keycloak realm needs neither.
type RuntimeConfig = { oidcAuthority?: string; oidcClientId?: string; oidcScope?: string; oidcAudience?: string; oidcOrganization?: string };
const runtime = ((window as unknown as { __STARTERAPP_CONFIG__?: RuntimeConfig }).__STARTERAPP_CONFIG__ ?? {});
const present = (value: string | undefined): string | undefined => (value && !value.startsWith("${") && !/replace-me/i.test(value) ? value : undefined);

export const resourceScopes = ["products:read", "products:write", "customers:read", "customers:write", "orders:read", "orders:write"] as const;

const authority = present(runtime.oidcAuthority)
  ?? present(import.meta.env.VITE_OIDC_AUTHORITY as string | undefined)
  ?? (import.meta.env.DEV ? "http://localhost:8090/realms/starterapp" : undefined);
const clientId = present(runtime.oidcClientId) ?? (import.meta.env.VITE_OIDC_CLIENT_ID as string | undefined) ?? "starterapp-web";
const scope = present(runtime.oidcScope) ?? (import.meta.env.VITE_OIDC_SCOPE as string | undefined) ?? `openid ${resourceScopes.join(" ")}`;
const audience = present(runtime.oidcAudience) ?? present(import.meta.env.VITE_OIDC_AUDIENCE as string | undefined);
const organization = present(runtime.oidcOrganization) ?? present(import.meta.env.VITE_OIDC_ORGANIZATION as string | undefined);

if (!authority) throw new Error("VITE_OIDC_AUTHORITY (or OIDC_AUTHORITY on a hosted instance) names the identity provider's realm; the application has no other way to know who you are.");

export const issuer: string = authority;

const extraQueryParams = { ...(audience ? { audience } : {}), ...(organization ? { organization } : {}) };

const manager = new UserManager({
  authority,
  client_id: clientId,
  redirect_uri: `${window.location.origin}/callback`,
  post_logout_redirect_uri: window.location.origin,
  response_type: "code",
  scope,
  automaticSilentRenew: true,
  userStore: new WebStorageStateStore({ store: window.sessionStorage }),
  ...(Object.keys(extraQueryParams).length > 0 ? { extraQueryParams } : {}),
});

let current: User | null = null;

const rememberWhereIAm = () => sessionStorage.setItem("starterapp:return-to", window.location.pathname + window.location.search);

// Starting the redirect never resolves: the page navigates away.
export async function ensureSignedIn(): Promise<void> {
  if (window.location.pathname === "/callback") {
    current = await manager.signinCallback().then((user) => user ?? null);
    const returnTo = sessionStorage.getItem("starterapp:return-to") ?? "/";
    sessionStorage.removeItem("starterapp:return-to");
    window.history.replaceState(null, "", returnTo);
    return;
  }
  current = await manager.getUser();
  if (!current || current.expired) {
    rememberWhereIAm();
    await manager.signinRedirect();
    await new Promise<never>(() => {});
  }
  manager.events.addUserLoaded((user) => { current = user; });
  manager.events.addAccessTokenExpired(() => { void manager.signinRedirect(); });
}

export function accessToken(): string | null {
  return current?.access_token ?? null;
}

export function claims(): Record<string, unknown> | null {
  const token = accessToken();
  if (!token) return null;
  const payload = token.split(".")[1];
  if (!payload) return null;
  return JSON.parse(atob(payload.replace(/-/g, "+").replace(/_/g, "/"))) as Record<string, unknown>;
}

export function signOut(): void {
  void manager.signoutRedirect();
}

export function signInWithScopes(scopes: readonly string[]): void {
  rememberWhereIAm();
  void manager.signinRedirect({ scope: `openid ${scopes.join(" ")}`.trim() });
}

export function stepUp(challenge: string | null): boolean {
  if (!challenge || !challenge.includes("insufficient_user_authentication")) return false;
  const acr = /acr_values="([^"]+)"/.exec(challenge)?.[1];
  rememberWhereIAm();
  void manager.signinRedirect(acr ? { acr_values: acr } : undefined);
  return true;
}
