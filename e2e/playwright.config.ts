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
  // Projects (https://playwright.dev/docs/test-projects), each run on its own fresh stack (story 7.1, e2e-fix): `journeys`
  // (e2e/tests/journeys) and the regression groups. The linking specs that need customer D's one link, D's three OTP sends an
  // hour or the production limits each get their own project; `regression` is every other spec. `npm test` runs
  // run-regression.sh, which runs them in order and fails a group that sent D more than three OTPs. Which group holds which spec:
  //   linking-rate-limits  linking-rate-limits.spec.ts
  //   linking-screens      linking-screens.spec.ts except its full-queue journey (D: 2 sends)
  //   linking-queue        linking-screens.spec.ts, the full-queue journey only (D: 3 sends)
  //   linking-verify-a     linking-verify.spec.ts, @verify-a: the code bounds, no-challenge, wrong-code lock (the reference
  //                        sequence the blank and released tests compare to), blank and released challenges (D: 2 sends)
  //   linking-verify-b     linking-verify.spec.ts, @verify-b: a later start supersedes, then the 5-minute expiry (D: 2 sends)
  //   linking-verify-c     linking-verify.spec.ts, @verify-c: the race for D (D: 2 sends)
  //   (the privacy scan test carries all three tags and runs in each)
  //   regression           every other spec
  projects: [
    { name: 'journeys', testMatch: 'journeys/**/*.spec.ts' },
    { name: 'linking-rate-limits', testMatch: 'linking-rate-limits.spec.ts' },
    { name: 'linking-screens', testMatch: 'linking-screens.spec.ts', grepInvert: /full queue:/ },
    { name: 'linking-queue', testMatch: 'linking-screens.spec.ts', grep: /full queue:/ },
    { name: 'linking-verify-a', testMatch: 'linking-verify.spec.ts', grep: /@verify-a/ },
    { name: 'linking-verify-b', testMatch: 'linking-verify.spec.ts', grep: /@verify-b/ },
    { name: 'linking-verify-c', testMatch: 'linking-verify.spec.ts', grep: /@verify-c/ },
    { name: 'regression', testIgnore: ['journeys/**', 'linking-rate-limits.spec.ts', 'linking-screens.spec.ts', 'linking-verify.spec.ts'] },
  ],
  use: {
    baseURL: process.env.APP_ORIGIN ?? 'http://localhost:8080',
    // No traces, videos or screenshots: they would capture cookies and personal data.
    trace: 'off',
    video: 'off',
    screenshot: 'off',
  },
});
