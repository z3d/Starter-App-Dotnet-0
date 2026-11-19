import type { Locator, Page } from '@playwright/test'
import { expect, heading, signInAs, test, users } from '../lib/fixtures'

// The layout follows the width alone, so a desktop browser in its responsive mode at this size shows the same.
const phone = { viewport: { width: 390, height: 844 }, deviceScaleFactor: 2 }

const noSideways = (page: Page) =>
  expect
    .poll(() => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth))
    .toBeLessThanOrEqual(0)

const height = async (locator: Locator) => (await locator.boundingBox())?.height ?? 0

test('every page works from a phone-sized window through the menu', async ({ browser }) => {
  const context = await browser.newContext(phone)
  const page = await context.newPage()
  await signInAs(page, users.dev)
  try {
    await page.goto('/')
    await expect(heading(page, 'The identity seam, end to end')).toBeVisible()
    await expect(page.getByRole('complementary')).toBeHidden()
    await noSideways(page)

    const button = page.getByRole('main').getByRole('button', { name: 'Create the product' })
    expect(await height(button)).toBeGreaterThanOrEqual(44)
    const field = page.getByRole('main').getByRole('textbox').first()
    expect(await height(field)).toBeGreaterThanOrEqual(44)
    expect(await field.evaluate((el) => getComputedStyle(el).fontSize)).toBe('16px')

    const menuButton = page.getByRole('banner').getByRole('button', { name: 'Menu' })
    expect(await height(menuButton)).toBeGreaterThanOrEqual(44)
    await menuButton.click()
    const menu = page.getByRole('dialog', { name: 'Menu' })
    await expect(menu.getByRole('button', { name: 'Sign out' })).toBeVisible()
    await menu.getByRole('link', { name: 'Settings', exact: true }).click()
    await page.waitForURL(/\/settings$/)
    await expect(menu).toBeHidden()
    await expect(heading(page, 'Settings')).toBeVisible()
    await noSideways(page)

    await page.setViewportSize({ width: 1440, height: 900 })
    await expect(page.getByRole('complementary')).toBeVisible()
    await expect(page.getByRole('banner').getByRole('button', { name: 'Menu' })).toBeHidden()
    const wide = page.getByRole('complementary').getByRole('button', { name: /Search or jump to/ })
    expect(await height(wide)).toBeLessThan(44)
  } finally {
    await context.close()
  }
})
