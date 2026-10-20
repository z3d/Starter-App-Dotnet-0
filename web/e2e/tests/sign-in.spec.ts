import { expect, test } from '@playwright/test'
import { authority, signInAtKeycloak, users } from '../lib/fixtures'

// Signing in the way a person does: the app sends them to the identity provider with a PKCE
// authorisation request, they sign in there, and they come back to the page they asked for.
test.describe('signing in', () => {
  test('goes through the identity provider and comes back to the page asked for', async ({ page }) => {
    await page.goto('/?from=sign-in')
    await page.waitForURL(/\/realms\/starterapp\/protocol\/openid-connect\/auth/)
    const request = new URL(page.url())
    expect(request.origin + request.pathname).toBe(`${authority}/protocol/openid-connect/auth`)
    expect(request.searchParams.get('client_id')).toBe('starterapp-web')
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
})
