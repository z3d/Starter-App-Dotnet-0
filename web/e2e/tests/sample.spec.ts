import { deleteProductsNamed, expect, result, signInAtKeycloak, test, users } from '../lib/fixtures'

test("a product, a customer and an order, then a refusal decided by the token's scopes", async ({ page }) => {
  await page.goto('/')
  const name = await page.getByTestId('product-name').inputValue()
  try {
    await page.getByRole('button', { name: 'Create the product' }).click()
    await expect(result(page, 'product')).toContainText('POST /api/v1/products\n201')

    await page.getByRole('button', { name: 'Create the customer' }).click()
    await expect(result(page, 'customer')).toContainText('POST /api/v1/customers\n201')

    await page.getByRole('button', { name: 'Place the order' }).click()
    await expect(result(page, 'order')).toContainText('POST /api/v1/orders\n201')
    await expect(page.getByTestId('order-statement')).toContainText('Order total, pending')
    await expect(page.getByTestId('order-queue')).toContainText('Confirm the orderIt stays pending')

    await page.getByRole('button', { name: 'Confirm the order' }).click()
    await expect(result(page, 'order')).toContainText('"status": "Confirmed"')
    await expect(page.getByTestId('order-statement')).toContainText('Order total, confirmed')
    await expect(page.getByTestId('order-queue')).toHaveCount(0)

    await page.getByLabel('orders:write').uncheck()
    await page.getByRole('button', { name: 'Sign in again with these scopes' }).click()
    await page.waitForURL(/openid-connect\/auth/)
    expect(new URL(page.url()).searchParams.get('scope')).not.toContain('orders:write')
    await signInAtKeycloak(page, users.dev)
    await page.waitForURL(/\/$/)
    await expect(page.getByTestId('claims')).not.toContainText('orders:write')

    await page.getByRole('button', { name: 'Create the product' }).click()
    await expect(result(page, 'product')).toContainText('201')
    await page.getByRole('button', { name: 'Create the customer' }).click()
    await expect(result(page, 'customer')).toContainText('201')
    await page.getByRole('button', { name: 'Place the order' }).click()
    await expect(result(page, 'order')).toContainText('POST /api/v1/orders\n403')
    await expect(result(page, 'order')).toContainText('orders:write')
  } finally {
    await deleteProductsNamed(name)
  }
})
