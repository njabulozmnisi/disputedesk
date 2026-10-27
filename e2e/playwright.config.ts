import { defineConfig } from '@playwright/test';

// Runs against the local Compose stack (docker compose up --wait). QA runs it behind scripts/test-lock.sh.
export default defineConfig({
  testDir: './tests',
  workers: 1,
  fullyParallel: false,
  timeout: 60_000,
  reporter: [['list']],
  use: {
    baseURL: process.env.APP_ORIGIN ?? 'http://localhost:8080',
    // No traces, videos or screenshots: they would capture cookies and personal data.
    trace: 'off',
    video: 'off',
    screenshot: 'off',
  },
});
