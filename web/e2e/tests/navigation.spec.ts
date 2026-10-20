import { expect, heading, rail, test } from '../lib/fixtures'

// The rail reaches every page, and the shell knows who is signed in and for which tenant.
test('the rail reaches every page', async ({ page }) => {
  await page.goto('/')
  await expect(heading(page, 'The identity seam, end to end')).toBeVisible()
  await rail(page, 'Sample').click()
  await expect(heading(page, 'The identity seam, end to end')).toBeVisible()
})

test('the command palette jumps to a page and offers to sign out', async ({ page }) => {
  await page.goto('/')
  await page.getByRole('button', { name: /Search or jump to/ }).click()
  const palette = page.getByRole('dialog')
  await expect(palette).toBeVisible()
  await palette.getByRole('combobox').fill('sign')
  await expect(palette.getByRole('option', { name: 'Sign out' })).toBeVisible()
  await palette.getByRole('combobox').fill('sam')
  await palette.getByRole('option', { name: 'Sample' }).click()
  await expect(palette).toBeHidden()
})

test('the shell names the tenant and offers to sign out', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByTestId('tenant-id')).toHaveText('local-dev-tenant')
  await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible()
})
