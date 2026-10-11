import { afterEach, describe, expect, it, vi } from "vitest";

const signOut = vi.fn();
const stepUp = vi.fn(() => true);
let token: string | null = "token-123";
vi.mock("./auth", () => ({ accessToken: () => token, signOut, stepUp }));

// The client captures the global fetch when it is created, so the stand-in goes in first.
const fetch = vi.fn<typeof globalThis.fetch>();
vi.stubGlobal("fetch", fetch);
const { identity } = await import("./api");
const { createApi } = await import("@starterapp/api-client");
const api = createApi("http://api.test");
api.use(identity);

const answer = (status: number, headers: Record<string, string> = {}) =>
  fetch.mockImplementation(async () => new Response(status === 204 ? null : "{}", { status, headers: { "Content-Type": "application/json", ...headers } }));

describe("the API client", () => {
  afterEach(() => fetch.mockReset());

  it("sends the person's token and a correlation id on every request", async () => {
    answer(200);
    await api.GET("/api/v1/products");
    const request = fetch.mock.calls[0]![0] as Request;
    expect(request.headers.get("Authorization")).toBe("Bearer token-123");
    expect(request.headers.get("X-Correlation-ID")).toMatch(/^[0-9a-f]{32}$/);
  });

  it("sends nothing when nobody is signed in", async () => {
    token = null;
    answer(200);
    await api.GET("/api/v1/products");
    expect((fetch.mock.calls[0]![0] as Request).headers.has("Authorization")).toBe(false);
    token = "token-123";
  });

  it("ends the session on a 401", async () => {
    answer(401);
    await api.GET("/api/v1/products");
    expect(signOut).toHaveBeenCalled();
  });

  it("asks for the second factor when a write is refused with the step-up challenge", async () => {
    const challenge = 'Bearer error="insufficient_user_authentication", acr_values="urn:starterapp:loa:mfa"';
    answer(403, { "WWW-Authenticate": challenge });
    await api.POST("/api/v1/products", { body: { name: "Widget", description: "", price: 1, currency: "AUD", stock: 1 } as never });
    expect(stepUp).toHaveBeenCalledWith(challenge);
  });
});
