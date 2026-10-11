import { defineConfig, devices } from "@playwright/test";

const baseURL = process.env.BASE_URL ?? `http://localhost:${process.env.WEB_PORT ?? "5184"}`;
const apiUrl = process.env.API_URL ?? "http://localhost:5164";
const idpUrl = process.env.IDP_URL ?? "http://localhost:8090";

export default defineConfig({
  outputDir: "e2e/results",
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI ? [["list"], ["html", { open: "never", outputFolder: "e2e/report" }]] : [["list"]],
  use: {
    baseURL,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    colorScheme: "light",
  },
  projects: [
    { name: "chromium", testDir: "e2e/tests", use: { ...devices["Desktop Chrome"], viewport: { width: 1280, height: 900 } } },
    { name: "deployed", testDir: "e2e/deployed", use: { ...devices["Desktop Chrome"], viewport: { width: 1280, height: 900 } } },
  ],
  webServer: process.env.BASE_URL
    ? undefined
    : {
        command: `pnpm --filter @starterapp/web exec vite --port ${new URL(baseURL).port} --strictPort`,
        url: baseURL,
        // A server already on the port was built for other settings; --strictPort makes the clash say so.
        reuseExistingServer: false,
        timeout: 120_000,
        env: { VITE_API_URL: apiUrl, VITE_OIDC_AUTHORITY: `${idpUrl}/realms/starterapp` },
      },
});
