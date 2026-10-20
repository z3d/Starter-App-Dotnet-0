import { deleteProductsNamed, expect, result, setAmr, signInAtKeycloak, test, users } from '../lib/fixtures'

// The dev realm has no second factor: amr is a user attribute, raised through the admin API in its place.
test('a write without the second factor sends the person back to sign in, and after it the write is saved', async ({
  asPasswordOnly: page,
}) => {
  await page.goto('/')
  const name = await page.getByTestId('product-name').inputValue()
  try {
    await expect(page.getByTestId('claims')).toContainText('"pwd"')
    await page.getByRole('button', { name: 'Create the product' }).click()

    await page.waitForURL(/\/realms\/starterapp\/protocol\/openid-connect\/auth/)
    const request = new URL(page.url())
    expect(request.searchParams.get('client_id')).toBe('starterapp-web')
    expect(request.searchParams.get('acr_values')).toBe(process.env.STEP_UP_ACR ?? 'urn:starterapp-dev:loa:mfa')

    await setAmr(users.passwordOnly.username, ['pwd', 'mfa'])
    await signInAtKeycloak(page, users.passwordOnly)
    await page.waitForURL(/\/$/)
    await expect(page.getByTestId('claims')).toContainText('"mfa"')

    await page.getByRole('button', { name: 'Create the product' }).click()
    await expect(result(page, 'product')).toContainText('POST /api/v1/products\n201')
    expect(page.url()).not.toContain('/realms/')
  } finally {
    await setAmr(users.passwordOnly.username, ['pwd'])
    await deleteProductsNamed(name)
  }
})

test('a read is not challenged', async ({ asPasswordOnly: page }) => {
  await page.goto('/')
  await expect(page.getByTestId('claims')).toContainText('"sub": "password-only"')
  expect(page.url()).not.toContain('/realms/')
})
