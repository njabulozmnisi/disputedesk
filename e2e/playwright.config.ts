import { defineConfig } from '@playwright/test';

// Runs against the local Compose stack (docker compose up --wait). QA runs it behind scripts/test-lock.sh.
export default defineConfig({
  testDir: './tests',
  workers: 1,
  // A committed test.only fails the run on CI (TestConfig.forbidOnly); retries stay at their default of 0.
  forbidOnly: !!process.env.CI,
  fullyParallel: false,
  timeout: 60_000,
  reporter: [['list']],
  // Two projects, each run on its own fresh stack (story 7.1): `journeys` (e2e/tests/journeys) and `regression` (every other spec).
  projects: [
    { name: 'journeys', testMatch: 'journeys/**/*.spec.ts' },
    { name: 'regression', testIgnore: 'journeys/**' },
  ],
  use: {
    baseURL: process.env.APP_ORIGIN ?? 'http://localhost:8080',
    // No traces, videos or screenshots: they would capture cookies and personal data.
    trace: 'off',
    video: 'off',
    screenshot: 'off',
  },
});
