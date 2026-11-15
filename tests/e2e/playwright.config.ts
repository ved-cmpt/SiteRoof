import { defineConfig, devices } from '@playwright/test';
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';

// Load the repository .env for local runs (CI provides ConnectionStrings__SiteRoof directly).
const envFile = resolve(__dirname, '../../.env');
if (existsSync(envFile)) {
  for (const line of readFileSync(envFile, 'utf8').split(/\r?\n/)) {
    const i = line.indexOf('=');
    if (i > 0 && !line.trimStart().startsWith('#')) {
      const key = line.slice(0, i).trim();
      process.env[key] ??= line.slice(i + 1).trim();
    }
  }
}

const baseURL = process.env.E2E_BASE_URL ?? 'http://127.0.0.1:5080';

export default defineConfig({
  testDir: './specs',
  timeout: 90_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : [['list']],
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } }, testIgnore: /screenshots\.spec/ },
    { name: 'screenshots', use: { ...devices['Desktop Chrome'] }, testMatch: /screenshots\.spec/ },
  ],
  // Serves the production Angular build from the API (same origin, like the container).
  // Build the web app first: cd web && npm run build
  webServer: process.env.E2E_BASE_URL
    ? undefined
    : {
        command: 'dotnet run --project ../../api/SiteRoof.Api --no-launch-profile',
        url: `${baseURL}/health/ready`,
        reuseExistingServer: !process.env.CI,
        timeout: 180_000,
        env: {
          ASPNETCORE_ENVIRONMENT: 'Development',
          ASPNETCORE_URLS: baseURL,
          Spa__RootPath: resolve(__dirname, '../../web/dist/web/browser'),
          Erp__BaseUrl: baseURL,
          Outbox__BaseDelaySeconds: '2',
          Outbox__PollIntervalSeconds: '1',
          Demo__ProvisionPermitsPerWindow: '500',
          ConnectionStrings__SiteRoof: process.env.ConnectionStrings__SiteRoof ?? '',
        },
      },
});
