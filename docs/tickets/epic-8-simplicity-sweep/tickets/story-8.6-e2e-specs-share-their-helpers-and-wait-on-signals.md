---
id: 6
type: story
title: "E2E specs share their helpers and wait on signals, not time"
parent: epic-8-simplicity-sweep
covers: [R33, R35]
after: []
hitl: false
risk: low
refined: true
---

# E2E specs share their helpers and wait on signals, not time

## Description

Slice S5 of the mitigation plan (wave 2), with TST-10's e2e parts and wave 4's ESLint check. `e2e/tests/support.ts` already holds `signInContext` and `queryRows` as the third-case abstraction (7.2 review), but about 15 regression specs keep private copies, one of which has drifted, and `origin`, `axeTags` and an `until` poll are re-declared (TST-5). Thirteen `waitForTimeout` calls and a 3-second `beforeEach` sleep wait for time, which Playwright's own reference calls inherently flaky (TST-6). Three spec headers describe states that ended stories ago, and the resolved-409 withdraw case checks only the response, not what was written (TST-10). This story moves each matching copy onto the shared helpers, replaces each timed wait with a wait on a response, focus or event (or, for a claim that nothing happens, an explicit bounded wait that must time out), and adds ESLint so `waitForTimeout` does not return.

## Requirements

Verify: on a fresh stack, `npm test` (`e2e/run-regression.sh`, which resets the stack and runs its seven projects) and `npm run journeys` pass in `e2e/`, each run twice on its own fresh stack; `npm run lint` passes in `e2e/` and in CI's `e2e` job; `grep -rn waitForTimeout e2e/tests` finds nothing.

Inputs: none (no endpoint change).

1. **Each matching copy imports the shared helpers** (TST-5; R35)
   **Given** the private `signIn(browser, loginKey)` copies in `dispute-list`, `dispute-detail`, `dispute-raise`, `dispute-stored-events`, `dispute-withdraw`, `linking-start` (`:45`), `operations-read`, `operations-actions`, `outbox-relay`, `outbox-relay-failure-paths`, `seeded-disputes`, `sla-sweep`, `spa-dispute-status-badges`, `spa-disputes-history` and `transactions` (all `.spec.ts`), the private `rows<T>(sql)` copies, the specs that declare `origin` (`sign-in-and-out-audit.spec.ts:12`, which hard-codes it, included) and the six `axeTags` declarations
   **When** this story lands
   **Then** each copy whose body does what `signInContext`, `queryRows` or `origin` in `support.ts` does is deleted and the spec imports the shared one, and `axeTags` moves to `support.ts` and is imported by the specs that use it; `transactions.spec.ts` gets back the 60-second timeouts and the configured origin it had lost
   **And** these keep their own sign-in, each with a one-line comment naming why: `forwarded-headers.spec.ts:24` (extra headers, a route installed before sign-in), `linking-screens.spec.ts:85` (returns the signed-in page and records its requests), `spa-transactions.spec.ts:46` (a time zone), `sign-in-and-out-audit.spec.ts:33` and `session-cookie-never-renewed.spec.ts:35` (they test the sign-in itself), `bff-sign-in-and-out.spec.ts:15` and `linking-verify.spec.ts:52` (hosted-login and fixture flows), and the two `signInAs(browser, { username, password })` copies (`linking-rate-limits.spec.ts:94`, `linking-start.spec.ts:49`), which take a created login rather than a fixture key; a copy the builder finds differs from `signInContext` in any other way also stays, named in the report

2. **Polls use Playwright's own retrying assertions** (TST-5; R35)
   **Given** the `until(description, check, seconds)` loops (`linking-rate-limits.spec.ts:181`, `linking-screens.spec.ts:137`, `linking-start.spec.ts:453`)
   **When** this story lands
   **Then** each call becomes `expect.poll(…, { timeout })` or `expect(async () => …).toPass({ timeout })` with the same timeout, as 32 call sites already do, and the three `until` functions are deleted

3. **No test waits for a fixed time** (TST-6; R35)
   **Given** the 13 `waitForTimeout` calls in `linking-screens.spec.ts:317,327,383,423,544,550`, `dispute-raise.spec.ts:372,376`, `scalar-reference.spec.ts:142`, `spa-session-end.spec.ts:122,141`, `dispute-detail.spec.ts:733` and `spa-disputes-history.spec.ts:347`
   **When** this story lands
   **Then** a wait for something that happens waits on it: the CSP check (`scalar-reference.spec.ts:142`) waits with `page.waitForFunction` until a `script-src` violation is recorded; the fade-out (`dispute-detail.spec.ts:733`) waits until the dialog is hidden; the click-again branch (`dispute-raise.spec.ts:372`) waits with `await expect(summary).toBeVisible()` (or `toBeFocused()`) and clicks once
   **And** a claim that something does not happen within a window starts the matching wait before the action and expects it to time out, keeping today's window: a request (`linking-screens.spec.ts:317,327,383,423`, `spa-disputes-history.spec.ts:347`) with `await expect(page.waitForRequest(matcher, { timeout })).rejects.toThrow()`, the matcher naming the linking or list request the old count checked; a navigation (`linking-screens.spec.ts:544,550`, `spa-session-end.spec.ts:122`) with `page.waitForURL` or `page.waitForEvent('load')` the same way; a focus change (`spa-session-end.spec.ts:141`, `dispute-raise.spec.ts:376`) with `page.waitForFunction` on `document.activeElement` the same way; the assertions after each wait are unchanged
   **And** the five `setTimeout` sleeps that are not pacing helpers (`linking-rate-limits.spec.ts:355`, `sla-sweep.spec.ts:65,198,200`, `forwarded-headers.spec.ts:58`) stay, each with a one-line comment naming what it waits for if it has none

4. **No unexplained sleep between tests** (TST-6; R35)
   **Given** `dispute-list.spec.ts`'s `test.beforeEach` that sleeps 3 seconds (`dispute-list.spec.ts:88-90`)
   **When** this story lands
   **Then** it is removed; where a test sends enough requests to reach the gateway's limit of 10 a second with a burst of 20, it paces with the spec's existing `pace()` helper, whose comment names that limit

5. **Spec headers describe the code as it is** (TST-10; R35)
   **Given** the stale headers in `dispute-withdraw.spec.ts:4-8` ("no withdraw screen yet", "no outbox relay before story 4.1", "no resolve action exists yet"), `dispute-raise.spec.ts:9`, `dispute-stored-events.spec.ts:8` and `dispute-detail.spec.ts:10`
   **When** this story lands
   **Then** each header is corrected to what the spec does today, or the outdated sentence is removed

6. **Withdrawing a Resolved dispute writes nothing but its refusal** (TST-10; R19, FR-20)
   **Given** the existing case "a resolved dispute cannot be withdrawn" (`operations-actions.spec.ts:354-362`), which already withdraws its resolved dispute `d1` and checks the 409 `action-not-allowed` body with customer status `resolved`
   **When** it runs
   **Then** it also asserts, by `queryRows`, that the dispute's status, its stored events and its outbox rows are the same before and after the refused withdraw, and that exactly one new `Withdraw` / `Rejected` audit entry is written for it (the same cell the integration tests cover); no new dispute or transaction is used

7. **ESLint refuses timed waits in e2e** (wave 4 item 1; R33)
   **Given** a new `e2e/eslint.config.js` (flat config) with `typescript-eslint`'s parser, `eslint-plugin-playwright`'s `playwright/no-wait-for-timeout` set to `error`, and ESLint core `no-restricted-properties` set to `error` for `{ property: 'waitForTimeout' }` (the plugin rule checks only a receiver named `page`, so `second.waitForTimeout` passes it)
   **When** `npm run lint` runs in `e2e/`, locally and as a step of CI's `e2e` job right after `npm ci`
   **Then** it passes on the tree and fails on any `waitForTimeout`, whatever the receiver; only these two rules are enabled (not `flat/recommended`); no rule is disabled without a comment saying why (R34)

## Boundaries

- Must not change: what any spec asserts, apart from criterion 6's added assertions and the timeouts criterion 1 restores; the journeys project, including `journeys/screens.ts`'s own `axeTags` and `journeys/uj-1.spec.ts`'s own `rows` copy; `playwright.config.ts`'s projects; the stack.
- No `storageState` setup project: the audit names it as Playwright's documented option for signed-in tests, but many specs test the sign-in itself, and the plan's change is the shared helper. Recorded in Notes.
- Not here: `flat/recommended`'s other rules. On today's tree they add 11 errors (7 `prefer-web-first-assertions`, 2 `no-unsafe-references`, 2 `no-networkidle`, pre-build review 2026-11-04) whose fixes would change assertions; a later story may enable them.
- Not here: the `setTimeout` pacing helpers (`pace`, `pause`, `sleep`) that space requests under a rate limit.

## Supervisor checks

- Throwaway copy: make the linking OTP field send a request on each keystroke; criterion 3's absence check at `linking-screens.spec.ts:317` fails.
- Throwaway copy: add `await page.waitForTimeout(100)` to any spec; `npm run lint` fails. Add `await second.waitForTimeout(100)` (a non-`page` variable); it fails too.
- Throwaway copy: let withdraw of a Resolved dispute write a stored event before refusing; criterion 6 fails.

## Build facts

- Owner: qa (the qa agent owns `e2e/`).
- Tier: Small, test-only.
- Flags: none.
- Lane: QA (one stack, behind `scripts/test-lock.sh`).
- Mechanisms and sources:
  - `expect.poll` and `toPass`: <https://playwright.dev/docs/test-assertions>.
  - `waitForTimeout` ("Never wait for timeout in production"): <https://playwright.dev/docs/api/class-page#page-wait-for-timeout>; `waitForRequest`, `waitForURL`, `waitForEvent`, `waitForFunction` with `timeout`: <https://playwright.dev/docs/api/class-page>.
  - `eslint-plugin-playwright`, rule `no-wait-for-timeout`: <https://github.com/mskelton/eslint-plugin-playwright>.
  - ESLint `no-restricted-properties`: <https://eslint.org/docs/latest/rules/no-restricted-properties>; flat config: <https://eslint.org/docs/latest/use/configure/configuration-files>.
- Packages (devDependencies of `e2e/`, pinned exactly; the builder re-checks each per AGENTS.md, Dependency verification):
  - `eslint-plugin-playwright` 2.12.2 (latest stable on 2026-11-04; repository mskelton/eslint-plugin-playwright; peer `eslint >=8.40.0`).
  - `eslint` 10.14.0 (latest stable on 2026-11-04). `frontend/` pins 10.13.0; moving it is a frontend dependency bump outside this story.
  - `typescript-eslint` 8.72.1 (latest stable; the version `frontend/` pins).
  - `typescript` 6.0.3: the version `frontend/` pins, because `typescript-eslint` 8.72.1's peer range is `>=4.8.4 <6.1.0`, which excludes the latest 7.0.2.
  - Node 24.22.1, as the pre-build review checked.
- Touches:
  - `e2e/tests/support.ts`, the specs named in criteria 1 to 6
  - `e2e/package.json` (`lint` script, four devDependencies), `e2e/package-lock.json`, `e2e/eslint.config.js` (new)
  - `.github/workflows/ci.yml`: one `npm run lint` step in the `e2e` job (outside QA's guard paths: the supervisor lands this one line as a Small-tier tooling fix after QA's change, or a backend builder does)
  - `docs/verification/8.6-qa.md`
- Collision: 8.10 (`dispute-withdraw.spec.ts`, `operations-actions.spec.ts`), 8.11 (`operations-actions.spec.ts`), 8.12 (`operations-read.spec.ts`), 8.13 (`spa-session-end.spec.ts`) and 8.14 (`sla-sweep.spec.ts`) change specs this story touches, in their QA stages. 8.6 merges before them (wave 2 before wave 3); each later QA stage rebases onto it and keeps its shared imports.

## Pre-build

- Challenger: `docs/verification/8.6-challenge.md` (simplify).
- Review: `docs/verification/epic-8-prebuild-wave2b.md`, section 8.6. No security read.
- Decisions: none raised.
- Sofia, 2026-11-04: every `[story]` point folded. Review: core `no-restricted-properties` beside the plugin rule, with a non-`page` Supervisor check (criterion 7); only the wait rules, the 11 `flat/recommended` errors left out (criterion 7, Boundaries); absence by a wait that must time out, not `expect.poll` on a zero count (criterion 3); the sign-in copies that go and stay, with reasons (criterion 1); criterion 6 uses the existing resolved dispute, so no transaction is needed, and a Collision line for 8.10 to 8.14 (Build facts); `waitForTimeout` only, the five `setTimeout` sleeps commented, the journeys frozen, eslint 10.14.0 with the reason, `axeTags`, `origin` and the hard-coded origin (criteria 1, 3, Boundaries, Build facts). Challenger: a bounded window kept explicit (criterion 3); specialised sign-ins kept (criterion 1); the existing resolved-withdraw case extended (criterion 6); `npm test` is the seven-project regression run (Verify); applicable copies only (criterion 1); each wait's request and when its listener starts (criterion 3); customer B's existing fixture used, so no customer A transaction (criterion 6).

## References

- parent — docs/tickets/epic-8-simplicity-sweep/epic-8-simplicity-sweep.md, section Requirements
- audit — docs/verification/simplicity-audit/tests.md, TST-5, TST-6, TST-10
- plan — docs/verification/simplicity-audit/mitigation-plan.md, wave 2, S5; wave 4, item 1
- verification — docs/verification/7.2-review.md (the shared helpers' origin)

## Notes

- Risk low (proposed): test-only; each replaced wait waits on the signal the old sleep stood in for, or keeps its window as an explicit wait that must time out.
- Decision: 2026-11-04 (Sofia) — no `storageState` setup project in this story: the plan's change is importing the shared helper, and a setup project would change how most specs sign in, which is a separate change no finding requires.
- Decision: 2026-11-04 (Sofia) — the `ci.yml` lint step is one line outside QA's paths; it lands with the story as a Small-tier tooling change, so the guard is not widened for QA.
- Decision: 2026-11-04 (Sofia) — a claim that nothing happens keeps its window as `waitForRequest` (or `waitForURL`, `waitForEvent`, `waitForFunction`) expected to time out: the claim is about a span of time, as the challenger says, and the rejected wait states that span where a zero-count poll passes at once.
- Decision: 2026-11-04 (Sofia) — only the two wait rules are enabled, not `flat/recommended`: its other 11 findings would change what specs assert, which this story keeps.
- Decision: 2026-11-04 (Sofia) — criterion 6 extends the existing resolved-withdraw case (customer B's `d1`) instead of a new customer A fixture: it already reaches a Resolved dispute and the 409, so only the write checks were missing, and it needs no new transaction in `docs/verification/e2e-data-budget.md`.
- Decision: 2026-11-04 (Sofia) — the journeys stay frozen (Boundaries), so their `axeTags` and `rows` copies stay.
