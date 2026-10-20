// Visual check: sign in through the dev Keycloak realm, then screenshot the main screens in
// light, dark, and mobile. Run with the API on VITE_API_URL and Vite on BASE_URL:
//   BASE_URL=http://localhost:5173 node e2e/screenshots.mjs
import { chromium } from '@playwright/test'
import { mkdirSync } from 'node:fs'

const base = process.env.BASE_URL ?? 'http://localhost:5173'
const out = process.env.OUT_DIR ?? 'e2e/shots'
mkdirSync(out, { recursive: true })

const browser = await chromium.launch()

async function session({ colorScheme = 'light', viewport = { width: 1440, height: 900 }, tag = 'light' }) {
  const context = await browser.newContext({ colorScheme, viewport, ignoreHTTPSErrors: true, deviceScaleFactor: 2 })
  const page = await context.newPage()
  page.on('pageerror', (e) => console.error('pageerror', e.message))
  await page.goto(base)
  await page.waitForURL(/realms\/starterapp/, { timeout: 30_000 })
  await page.fill('#username', 'dev-user')
  await page.fill('#password', 'dev-password')
  await page.click('#kc-login')
  await page.waitForURL((u) => u.origin === new URL(base).origin && !u.pathname.startsWith('/auth'), {
    timeout: 30_000,
  })
  await page.waitForTimeout(1200)
  await page.screenshot({ path: `${out}/sample-${tag}.png`, fullPage: true })
  await context.close()
}

await session({ tag: 'light' })
await session({ colorScheme: 'dark', tag: 'dark' })
await session({ viewport: { width: 390, height: 844 }, tag: 'mobile' })
await browser.close()
console.log(`screenshots in ${out}/`)
