import { expect, test } from '@playwright/test'
import { deleteProductsNamed, result, signInAtKeycloak, users } from '../lib/fixtures'

// The nginx image: the app is served as static files, /api is proxied to the API on the same origin,
// and /config.js carries the identity provider's realm rendered at start-up.
test('signing in goes through the identity provider and a write goes through nginx to the API', async ({ page }) => {
  await page.goto('/')
  await page.waitForURL(/\/realms\/starterapp\/protocol\/openid-connect\/auth/)
  await signInAtKeycloak(page, users.dev)
  await page.waitForURL(/\/$/)
  await expect(page.getByRole('heading', { level: 1, name: 'The identity seam, end to end' })).toBeVisible()

  const name = await page.getByTestId('product-name').inputValue()
  try {
    await page.getByRole('button', { name: 'Create the product' }).click()
    await expect(result(page, 'product')).toContainText('POST /api/v1/products\n201')
  } finally {
    await deleteProductsNamed(name)
  }
})
