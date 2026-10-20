import { defineConfig, devices } from '@playwright/test'

// The browser tests (`npm run e2e`): the web app against a live API and the dev Keycloak, the way
// a person uses it. web/e2e/run-e2e.sh boots that stack on its own ports and runs them; against a
// running local rig, point BASE_URL, API_URL and IDP_URL at it. The `deployed` project drives the
// nginx image instead of the dev server (E2E_DEPLOYED=1 in run-e2e.sh).
//
// The dev server is started here, on WEB_PORT, unless BASE_URL names one already running.
const baseURL = process.env.BASE_URL ?? `http://localhost:${process.env.WEB_PORT ?? '5183'}`
const apiUrl = process.env.API_URL ?? 'https://localhost:7286'
const idpUrl = process.env.IDP_URL ?? 'http://localhost:8090'

export default defineConfig({
  outputDir: 'test-results',
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI ? [['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]] : [['list']],
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    timezoneId: 'Australia/Brisbane',
    locale: 'en-AU',
    colorScheme: 'light',
  },
  projects: [
    {
      name: 'chromium',
      testDir: 'e2e/tests',
      use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } },
    },
    {
      name: 'deployed',
      testDir: 'e2e/deployed',
      use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } },
    },
  ],
  webServer: process.env.BASE_URL
    ? undefined
    : {
        command: `npx vite --port ${new URL(baseURL).port} --strictPort`,
        url: baseURL,
        reuseExistingServer: !process.env.CI,
        timeout: 120_000,
        env: { VITE_API_URL: apiUrl, VITE_OIDC_AUTHORITY: `${idpUrl}/realms/starterapp` },
      },
})
