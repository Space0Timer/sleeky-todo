import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './e2e',
  globalSetup: './e2e/global-setup.ts',
  globalTeardown: './e2e/global-teardown.ts',
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  timeout: 60_000,
  workers: process.env.CI ? 1 : undefined,
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
        MongoDb__DatabaseName: 'sleekyTodoPlaywright',
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
