import { beforeEach, describe, expect, it, vi } from "vitest";

const signinRedirect = vi.fn(async () => {});
const getUser = vi.fn(async () => null);
const { constructed } = vi.hoisted(() => ({ constructed: [] as Record<string, unknown>[] }));
vi.mock("oidc-client-ts", () => ({
  UserManager: class {
    constructor(settings: Record<string, unknown>) { constructed.push(settings); }
    events = { addUserLoaded: vi.fn(), addAccessTokenExpired: vi.fn() };
    signinRedirect = signinRedirect;
    getUser = getUser;
    signinCallback = vi.fn(async () => null);
    signoutRedirect = vi.fn(async () => {});
  },
  WebStorageStateStore: class {},
}));

const auth = await import("./auth");

describe("stepUp", () => {
  beforeEach(() => signinRedirect.mockClear());

  it("does nothing for an ordinary refusal", () => {
    expect(auth.stepUp(null)).toBe(false);
    expect(auth.stepUp('Bearer error="insufficient_scope"')).toBe(false);
    expect(signinRedirect).not.toHaveBeenCalled();
  });

  it("sends the person back to sign in at the level the API asked for, and remembers where they were", () => {
    window.history.replaceState(null, "", "/orders?tab=open");
    const challenge = 'Bearer error="insufficient_user_authentication", error_description="A second factor is required", acr_values="urn:starterapp:loa:mfa"';
    expect(auth.stepUp(challenge)).toBe(true);
    expect(signinRedirect).toHaveBeenCalledWith({ acr_values: "urn:starterapp:loa:mfa" });
    expect(sessionStorage.getItem("starterapp:return-to")).toBe("/orders?tab=open");
  });

  it("still signs in again when the challenge names no level", () => {
    expect(auth.stepUp('Bearer error="insufficient_user_authentication"')).toBe(true);
    expect(signinRedirect).toHaveBeenCalledWith(undefined);
  });
});

describe("signInWithScopes", () => {
  beforeEach(() => signinRedirect.mockClear());

  it("asks the provider for exactly the scopes chosen", () => {
    auth.signInWithScopes(["products:read", "orders:read"]);
    expect(signinRedirect).toHaveBeenCalledWith({ scope: "openid products:read orders:read" });
  });
});

describe("the provider's settings", () => {
  it("signs in with the authorization code flow against the configured realm", () => {
    expect(constructed[0]).toMatchObject({ authority: "http://idp.test/realms/starterapp", client_id: "starterapp-web", response_type: "code" });
    expect(constructed[0]).not.toHaveProperty("extraQueryParams");
  });

  it("asks an Auth0 tenant for the API's audience and the Organization", async () => {
    vi.resetModules();
    (window as unknown as { __STARTERAPP_CONFIG__?: unknown }).__STARTERAPP_CONFIG__ = { oidcAudience: "https://api.starterapp.example", oidcOrganization: "org_aB3dE5fG7hI9jK1l" };
    try {
      await import("./auth");
      expect(constructed.at(-1)).toMatchObject({ extraQueryParams: { audience: "https://api.starterapp.example", organization: "org_aB3dE5fG7hI9jK1l" } });
    } finally {
      delete (window as unknown as { __STARTERAPP_CONFIG__?: unknown }).__STARTERAPP_CONFIG__;
    }
  });

  it("ignores a REPLACE-ME value an operator did not overwrite, like the API does", async () => {
    vi.resetModules();
    (window as unknown as { __STARTERAPP_CONFIG__?: unknown }).__STARTERAPP_CONFIG__ = { oidcAuthority: "https://REPLACE-ME/realms/starterapp" };
    try {
      await import("./auth");
      expect(constructed.at(-1)).toMatchObject({ authority: "http://idp.test/realms/starterapp" });
    } finally {
      delete (window as unknown as { __STARTERAPP_CONFIG__?: unknown }).__STARTERAPP_CONFIG__;
    }
  });

  it("ignores a placeholder nginx did not render", async () => {
    vi.resetModules();
    (window as unknown as { __STARTERAPP_CONFIG__?: unknown }).__STARTERAPP_CONFIG__ = { oidcAuthority: "${OIDC_AUTHORITY}", oidcClientId: "${OIDC_CLIENT_ID}" };
    try {
      await import("./auth");
      expect(constructed.at(-1)).toMatchObject({ authority: "http://idp.test/realms/starterapp", client_id: "starterapp-web" });
    } finally {
      delete (window as unknown as { __STARTERAPP_CONFIG__?: unknown }).__STARTERAPP_CONFIG__;
    }
  });
});

describe("ensureSignedIn", () => {
  it("starts the redirect when nobody is signed in and never resolves", async () => {
    window.history.replaceState(null, "", "/products");
    let settled = false;
    void auth.ensureSignedIn().then(() => { settled = true; });
    await vi.waitFor(() => expect(signinRedirect).toHaveBeenCalled());
    expect(sessionStorage.getItem("starterapp:return-to")).toBe("/products");
    expect(settled).toBe(false);
    expect(auth.accessToken()).toBeNull();
    expect(auth.claims()).toBeNull();
  });
});
