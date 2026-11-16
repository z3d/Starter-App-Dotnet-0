import { apiAs, apiUrl, expect, rail, test, tokensFor, users } from '../lib/fixtures'

const html = (page: import('@playwright/test').Page) => page.locator('html')

test.afterAll(async () => {
  await (await apiAs(users.dev)).put('/api/v1/settings/appearance', { palette: 'teal' })
})

test('a person switches to dark and back to the system, and the choice outlives a reload', async ({ page }) => {
  await page.goto('/')
  await page.getByRole('button', { name: 'Appearance' }).click()
  await page.getByRole('menuitemradio', { name: 'Dark' }).click()
  await expect(html(page)).toHaveAttribute('data-theme', 'dark')

  await page.reload()
  await expect(html(page)).toHaveAttribute('data-theme', 'dark')
  expect(await page.evaluate(() => getComputedStyle(document.documentElement).colorScheme)).toBe('dark')

  await page.getByRole('button', { name: 'Appearance' }).click()
  await expect(page.getByRole('menuitemradio', { name: 'Dark' })).toHaveAttribute('aria-checked', 'true')
  await page.getByRole('menuitemradio', { name: 'System' }).click()
  await page.reload()
  await expect(html(page)).not.toHaveAttribute('data-theme')
})

test("an administrator changes the tenant's colours, and a colleague sees them after a reload", async ({
  page,
  asPasswordOnly,
}) => {
  await (await apiAs(users.dev)).put('/api/v1/settings/appearance', { palette: 'teal' })

  await page.goto('/')
  await rail(page, 'Settings').click()
  await page.getByRole('radio', { name: /Sky/ }).click()
  await page.getByRole('button', { name: 'Save for everyone' }).click()
  await expect(html(page)).toHaveAttribute('data-palette', 'sky')
  await expect(page.getByTestId('appearance-changed')).toContainText('Last changed by dev-user')

  await page.reload()
  await expect(html(page)).toHaveAttribute('data-palette', 'sky')

  await asPasswordOnly.goto('/')
  await expect(html(asPasswordOnly)).toHaveAttribute('data-palette', 'sky')
  await asPasswordOnly.reload()
  await expect(html(asPasswordOnly)).toHaveAttribute('data-palette', 'sky')
  const accent = await asPasswordOnly.evaluate(() =>
    getComputedStyle(document.documentElement).getPropertyValue('--accent').trim(),
  )
  expect(accent).toBe('oklch(0.54 0.134 248)')
})

test('someone who is not an administrator has no picker, and the API refuses their change', async ({
  asPasswordOnly,
}) => {
  await asPasswordOnly.goto('/')
  await expect(rail(asPasswordOnly, 'Settings')).toHaveCount(0)
  await asPasswordOnly.goto('/settings')
  await expect(asPasswordOnly.getByText('Only an administrator can change these')).toBeVisible()
  await expect(asPasswordOnly.getByRole('button', { name: 'Save for everyone' })).toHaveCount(0)

  const { access_token, scope } = await tokensFor(users.passwordOnly)
  expect(scope.split(' ')).toContain('settings:read')
  expect(scope.split(' ')).not.toContain('settings:write')
  const response = await fetch(`${apiUrl}/api/v1/settings/appearance`, {
    method: 'PUT',
    headers: { Authorization: `Bearer ${access_token}`, 'Content-Type': 'application/json' },
    body: JSON.stringify({ palette: 'coral' }),
  })
  expect(response.status).toBe(403)
  expect(response.headers.get('www-authenticate')).toContain('insufficient_scope')
})
