import { expect, test } from '@playwright/test'
import { authority, sessionCookie, signInAs, signInAtKeycloak, users } from '../lib/fixtures'

// Signing in the way a person does: the API sends them to the identity provider with a PKCE
// authorisation request, they sign in there, and they come back to the page they asked for.
test.describe('signing in', () => {
  test('goes through the identity provider and comes back to the page asked for', async ({ page }) => {
    await page.goto('/?from=sign-in')
    await page.waitForURL(/\/realms\/starterapp\/protocol\/openid-connect\/auth/)
    const request = new URL(page.url())
    expect(request.origin + request.pathname).toBe(`${authority}/protocol/openid-connect/auth`)
    expect(request.searchParams.get('client_id')).toBe('starterapp-session')
    expect(request.searchParams.get('response_type')).toBe('code')
    expect(request.searchParams.get('code_challenge_method')).toBe('S256')

    await signInAtKeycloak(page, users.dev)

    await page.waitForURL(/\/\?from=sign-in$/)
    await expect(page.getByRole('heading', { level: 1, name: 'The identity seam, end to end' })).toBeVisible()
    await expect(page.getByRole('navigation', { name: 'Primary' })).toBeVisible()
  })

  test('a wrong password stays at the identity provider', async ({ page }) => {
    await page.goto('/')
    await page.waitForURL(/openid-connect\/auth/)
    await signInAtKeycloak(page, { username: users.dev.username, password: 'not-the-password' })
    await expect(page.locator("#input-error, .kc-feedback-text, [id^='input-error']").first()).toBeVisible()
    expect(page.url()).toContain('/realms/starterapp/')
  })

  test('leaves the browser holding a cookie its scripts cannot read, and no token anywhere', async ({ page }) => {
    await signInAs(page, users.dev)
    await page.goto('/')
    await expect(page.getByRole('heading', { level: 1, name: 'The identity seam, end to end' })).toBeVisible()

    const cookie = (await page.context().cookies()).find((c) => c.name === sessionCookie)!
    expect(cookie.httpOnly).toBe(true)
    expect(cookie.secure).toBe(true)
    expect(cookie.sameSite).toBe('Strict')
    const seen = await page.evaluate(() => ({
      cookie: document.cookie,
      stored: JSON.stringify({ ...window.sessionStorage, ...window.localStorage }),
    }))
    expect(seen.cookie).not.toContain(sessionCookie)
    expect(seen.stored).not.toMatch(/eyJ[\w-]+\.eyJ/)
  })

  test('signing out ends the session here and at the identity provider', async ({ page }) => {
    await page.goto('/')
    await page.waitForURL(/openid-connect\/auth/)
    await signInAtKeycloak(page, users.dev)
    await page.waitForURL(/\/$/)

    await page.getByRole('button', { name: 'Sign out' }).click()
    await page.waitForURL(/\/signed-out\?reason=signed-out/)
    await expect(page.getByText('You are signed out')).toBeVisible()
    expect((await page.context().cookies()).some((c) => c.name === sessionCookie)).toBe(false)

    await page.goto('/')
    await page.waitForURL(/openid-connect\/auth/)
    await expect(page.locator('#username')).toBeVisible()
  })
})
