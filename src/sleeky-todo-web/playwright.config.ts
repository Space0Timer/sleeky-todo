import { defineConfig, devices } from '@playwright/test'

import { databaseName } from './e2e/database-name.ts'

export default defineConfig({
  testDir: './e2e',
  globalSetup: './e2e/global-setup.ts',
  globalTeardown: './e2e/global-teardown.ts',
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  timeout: 60_000,
  // One worker everywhere, not just in CI. Every spec shares one database, so
  // running files in parallel would let one test's cleanup reach into another's
  // data — and a local run that behaves differently from CI is how an ordering
  // bug reaches CI in the first place.
  workers: 1,
  // CI annotates the pull request through the github reporter, keeps JUnit for
  // the run summary, and writes the HTML report as an artifact rather than
  // trying to open it on a machine with no browser.
  reporter: process.env.CI
    ? [
        ['github'],
        ['junit', { outputFile: 'test-results/junit.xml' }],
        ['html', { open: 'never' }],
      ]
    : 'html',
  use: {
    baseURL: 'http://127.0.0.1:5173',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: [
    {
      command: 'dotnet run --project ../Sleeky.Todo.Api --launch-profile https',
      url: 'https://localhost:7238/health',
      ignoreHTTPSErrors: true,
      reuseExistingServer: false,
      timeout: 120_000,
      env: {
        ...process.env,
        MongoDb__DatabaseName: databaseName,
      },
    },
    {
      command: 'corepack yarn dev --host 127.0.0.1',
      url: 'http://127.0.0.1:5173',
      reuseExistingServer: false,
      timeout: 60_000,
    },
  ],
})
