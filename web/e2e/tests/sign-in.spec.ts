import { expect, test } from "@playwright/test";
import { authority, clientId, signInAtKeycloak, users } from "../lib/fixtures";

test.describe("signing in", () => {
  test("goes through the identity provider and comes back to the page asked for", async ({ page }) => {
    await page.goto("/?step=order");
    await page.waitForURL(/\/realms\/starterapp\/protocol\/openid-connect\/auth/);
    const request = new URL(page.url());
    expect(request.origin + request.pathname).toBe(`${authority}/protocol/openid-connect/auth`);
    expect(request.searchParams.get("client_id")).toBe(clientId);
    expect(request.searchParams.get("response_type")).toBe("code");
    expect(request.searchParams.get("code_challenge_method")).toBe("S256");
    expect(request.searchParams.get("code_challenge")).toBeTruthy();
    expect(request.searchParams.get("scope")).toContain("orders:write");

    await signInAtKeycloak(page, users.dev);

    await page.waitForURL(/\/\?step=order$/);
    await expect(page.getByRole("heading", { level: 1, name: "StarterApp" })).toBeVisible();
    await expect(page.getByRole("banner")).toContainText(users.dev.username);
    await expect(page.getByTestId("claims")).toContainText('"tid": "local-dev-tenant"');
  });

  test("a wrong password stays at the identity provider", async ({ page }) => {
    await page.goto("/");
    await page.waitForURL(/openid-connect\/auth/);
    await signInAtKeycloak(page, { username: users.dev.username, password: "not-the-password" });
    await expect(page.locator("#input-error, .kc-feedback-text, [id^='input-error']").first()).toBeVisible();
    expect(page.url()).toContain("/realms/starterapp/");
  });
});
