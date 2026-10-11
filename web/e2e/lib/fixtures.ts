import { test as base, expect, type Page } from "@playwright/test";

export const apiUrl = process.env.API_URL ?? "http://localhost:5164";
export const idpUrl = process.env.IDP_URL ?? "http://localhost:8090";
export const realm = "starterapp";
export const authority = `${idpUrl}/realms/${realm}`;
export const clientId = "starterapp-web";
export const resourceScopes = "products:read products:write customers:read customers:write orders:read orders:write";

export const users = {
  dev: { username: "dev-user", password: "dev-password" },
  // Its token carries amr ["pwd"] alone, so every write it makes answers the step-up challenge.
  passwordOnly: { username: "password-only", password: "dev-password" },
} as const;
export type DevUser = (typeof users)[keyof typeof users];

interface TokenResponse {
  access_token: string;
  id_token?: string;
  refresh_token?: string;
  token_type: string;
  scope: string;
  expires_in: number;
  session_state?: string;
}

const tokens = new Map<string, Promise<TokenResponse>>();

export function tokensFor(user: DevUser): Promise<TokenResponse> {
  let pending = tokens.get(user.username);
  if (!pending) {
    pending = (async () => {
      const response = await fetch(`${authority}/protocol/openid-connect/token`, {
        method: "POST",
        headers: { "Content-Type": "application/x-www-form-urlencoded" },
        body: new URLSearchParams({ grant_type: "password", client_id: clientId, scope: `openid ${resourceScopes}`, username: user.username, password: user.password }),
      });
      if (!response.ok) throw new Error(`the dev Keycloak at ${idpUrl} answered ${response.status} for ${user.username}: ${await response.text()}`);
      return (await response.json()) as TokenResponse;
    })();
    tokens.set(user.username, pending);
  }
  return pending;
}

const decode = (jwt: string) => JSON.parse(Buffer.from(jwt.split(".")[1]!, "base64url").toString()) as Record<string, unknown>;

export async function signInAs(page: Page, user: DevUser): Promise<void> {
  const t = await tokensFor(user);
  const stored = {
    id_token: t.id_token,
    session_state: t.session_state ?? null,
    access_token: t.access_token,
    refresh_token: t.refresh_token,
    token_type: t.token_type,
    scope: t.scope,
    profile: decode(t.id_token ?? t.access_token),
    expires_at: Math.floor(Date.now() / 1000) + t.expires_in,
  };
  await page.addInitScript(
    ([key, value]) => {
      try {
        window.sessionStorage.setItem(key!, value!);
      } catch {
        // Keycloak's own pages run this too; they do not matter.
      }
    },
    [`oidc.user:${authority}:${clientId}`, JSON.stringify(stored)],
  );
}

export async function apiAs(user: DevUser) {
  const { access_token } = await tokensFor(user);
  const call = async (method: string, path: string, body?: unknown, tolerated: number[] = []) => {
    const response = await fetch(`${apiUrl}${path}`, {
      method,
      headers: { Authorization: `Bearer ${access_token}`, ...(body === undefined ? {} : { "Content-Type": "application/json" }) },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    const text = await response.text();
    if (!response.ok && !tolerated.includes(response.status)) throw new Error(`${method} ${path} answered ${response.status}: ${text.slice(0, 300)}`);
    return text ? (JSON.parse(text) as unknown) : null;
  };
  return {
    get: <T>(path: string) => call("GET", path) as Promise<T>,
    post: <T>(path: string, body: unknown = {}) => call("POST", path, body) as Promise<T>,
    put: <T>(path: string, body: unknown) => call("PUT", path, body) as Promise<T>,
    // A product an order holds answers 409 and stays; the stack goes with the run.
    delete: (path: string) => call("DELETE", path, undefined, [409]),
  };
}

// Keycloak 26 hides an attribute the user profile does not declare, so the policy is opened for the write.
export async function setAmr(username: string, amr: string[]): Promise<void> {
  const admin = async (method: string, path: string, body?: unknown) => {
    const response = await fetch(`${idpUrl}/admin/realms/${realm}${path}`, { method, headers: { Authorization: `Bearer ${adminToken}`, ...(body === undefined ? {} : { "Content-Type": "application/json" }) }, body: body === undefined ? undefined : JSON.stringify(body) });
    if (!response.ok) throw new Error(`Keycloak's admin API answered ${response.status} for ${method} ${path}: ${await response.text()}`);
    const text = await response.text();
    return text ? (JSON.parse(text) as Record<string, unknown>) : null;
  };
  const tokenResponse = await fetch(`${idpUrl}/realms/master/protocol/openid-connect/token`, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ grant_type: "password", client_id: "admin-cli", username: process.env.KEYCLOAK_ADMIN_USER ?? "admin", password: process.env.KEYCLOAK_ADMIN_PASSWORD ?? "admin" }),
  });
  if (!tokenResponse.ok) throw new Error(`the dev Keycloak at ${idpUrl} refused its admin (${tokenResponse.status})`);
  const adminToken = ((await tokenResponse.json()) as { access_token: string }).access_token;
  const profile = (await admin("GET", "/users/profile"))!;
  await admin("PUT", "/users/profile", { ...profile, unmanagedAttributePolicy: "ADMIN_EDIT" });
  try {
    const [user] = (await admin("GET", `/users?exact=true&username=${encodeURIComponent(username)}`)) as unknown as Array<{ id: string }>;
    if (!user) throw new Error(`${username} is not in the dev realm`);
    const full = (await admin("GET", `/users/${user.id}`))! as { attributes?: Record<string, string[]> };
    await admin("PUT", `/users/${user.id}`, { ...full, attributes: { ...full.attributes, amr } });
  } finally {
    await admin("PUT", "/users/profile", profile);
  }
}

export async function signInAtKeycloak(page: Page, user: { username: string; password: string }): Promise<void> {
  await page.locator("#username").fill(user.username);
  await page.locator("#password").fill(user.password);
  await page.locator("#kc-login").click();
}

export async function deleteProductsNamed(prefix: string): Promise<void> {
  const api = await apiAs(users.dev);
  const { data } = await api.get<{ data: Array<{ id: number; name: string }> }>("/api/v1/products?page=1&pageSize=100");
  for (const product of data.filter((p) => p.name.startsWith(prefix))) await api.delete(`/api/v1/products/${product.id}`);
}

export const test = base.extend<{ asPasswordOnly: Page }>({
  page: async ({ page }, use) => {
    await signInAs(page, users.dev);
    await use(page);
  },
  asPasswordOnly: async ({ browser }, use) => {
    const context = await browser.newContext();
    const page = await context.newPage();
    await signInAs(page, users.passwordOnly);
    await use(page);
    await context.close();
  },
});

export { expect };

export const result = (page: Page, step: string) => page.getByTestId(`${step}-result`);
