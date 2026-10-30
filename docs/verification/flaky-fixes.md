# Intermittent test failures awaiting a fix

Each needs its root cause found and fixed in the test (or the code, if the cause is there), with a test that fails without the fix. Test-only fixes go in under the standing approval.

1. **`e2e/tests/bff-sign-in-and-out.spec.ts:92`** (3.3 QA rerun, first test after fresh provisioning): after logout the browser context still held a `disputedesk-session` cookie (expires -1); passed 3 of 3 on rerun. Suspected cause: a request in flight during logout renews the session cookie (`OnValidatePrincipal` sets `ShouldRenew` on every request). The session row is deleted, so the cookie is dead (401), but find the cause before calling it test-only.

## Later test-only items

Not intermittent; test-only changes queued here so no story carries them.

1. **`Bff.SignOutTests.Login_AlreadySignedIn_RecordsSignOutForTheEndedSessionBeforeTheNewSignIn`** (2.8 review nit, moved out of 3.4 on 2026-10-30): the name promises an order the test does not assert. Rename it to drop "BeforeTheNewSignIn", or assert the order.
3. **`infra/zitadel/provision/provision.sh` and `verify.sh` curl calls** (2.6 review): add `--max-time` to `CURL_OPTS` so every provisioning network call has a timeout; tooling only. With it, `verify.sh:79` prints `fontUrl`, `logoUrl` and `logoUrlDark` instead of the joined `logos` detail, and the `provision` service comment in `docker-compose.yml` names the label policy.
4. **`RaiseScreen.test.tsx`, "takes focus on the summary even when a field error already shows after blur"** (3.6 round 3 delta review): in jsdom the test passes without the fix. Attach a `vi.fn()` `focus` listener to the note before pressing Continue and assert it is never called (fails without `shouldFocusError: false`, verified by the reviewer). Also add `frontend/tsconfig.tsbuildinfo` to `.gitignore`.

## Done 2026-10-30

Items 1 (renamed to `Login_AlreadySignedIn_RecordsSignOutForTheEndedSession`: on a frozen clock the two rows share `OccurredAtUtc` and the v7 ID order is random, so the order cannot be asserted), 3 (`--max-time 30` in both provisioning scripts, guarded by `PlatformScriptTests.ProvisioningScript_CurlOptions_SetAMaxTime`; the `verify.sh` logos line and the compose comment follow 2.6) and 4 (the note never receives focus; `frontend/.gitignore` ignores `tsconfig.tsbuildinfo`, since the guard keeps frontend builders out of the root `.gitignore`). Item 2 waits for 3.9 to merge (the MSBuild alias rule is 3.9's code).
- 3.7 DisputesScreen.test.tsx (round 2 review, nits): comment at :237 ("first in the Open section" means first on the page), comment at :255 ("older day shown" is the dateTime attribute), optional explicit createdAt on firstPage.
- 2.6 CI escape (2026-10-30): `PlatformScriptTests.ProvisionScript_RetryOptions_SucceedAfterTwo503Answers` fails on the Linux runner every time (edb1d71, run 41023548303, two attempts) and passes on macOS; the builder, verifier, reviewer and rebase ran only on macOS. Category: environment or tooling. Countermeasure to propose at the retrospective: tests that shell out (curl, bash) run once in the Linux SDK container before a story reports built.
- e2e data budget (3.7 QA, 2026-10-30; recurring since 3.6): a full e2e run on a fresh database raises disputes on 17 of customer A's 20 disputable transactions before `outbox-relay-failure-paths.spec.ts:129` and `transactions.spec.ts:129` run, so they fail (2 failed, 6 not run); each passes alone. 5.4 seeds 7 disputes on ACC-A-1, leaving 13. Fix before 5.4's QA: specs that raise disputes take their transaction from a per-spec reserved set in the fixtures (or customer D after linking), not "the first disputable row".
- 6.1 OtpSends.ClaimAsync (6.2 security L2, 2026-10-30): a challenge locked by three wrong guesses while its start job is pending still gets an SMS (ClaimAsync does not check Attempts). Not exploitable; fold into 6.3 (it touches the linking job) as one condition and one test.
- 6.6 review low (2026-10-30): LinkingEndpoints.cs:47-48 comment still says forwarded headers are trusted from story 6.3; update it in 6.3, which touches the file.
- linking-start.spec.ts:338 (5.4 QA F2, 2026-10-30): the privacy check matched a six-digit OTP inside a random hex TraceId in an audit row; match OTPs as whole tokens (word boundaries) or exclude TraceId columns. Test-only.
- 5.3 SourceRulesTests.cs:88 (security N1, 2026-10-30): LineComment() blanks everything after // on any line, including inside string literals; blank only whole-line comments ((?m)^\s*//.*$). Test-only.

## Done 2026-10-30 (batch tf-1)

- Intermittent 1 (SignInTests cookie check): already fixed on main by 1b5fc87 (exact ID and access tokens absent from headers and body).
- Later 2 (MSBuild alias rule): parsed with `XDocument.Load`, case-insensitive `Using` items with an `Alias` attribute or child element.
- 2.6 label-policy.sh: a duplicate split by a newline before its colon is counted; the value `sed` is anchored to the match start.
- 6.6 security L-a: the identity access-log test slices from `server {` and requires the server-level line.
- 5.4 InvalidFixtureDisputesTests: the SLA-demo count cases match the count message.
- Gateway `Forwarded` header: `proxy_set_header Forwarded "";` in all 8 proxied locations, guarded by `PlatformScriptTests.GatewayTemplate_ProxiedLocations_DropTheClientsForwardedHeader`.
- OutboxRelayHeartbeatTests: bound 11 s to 12 s (the 10 s send timeout runs on librdkafka's real clock, so a fake clock cannot drive it; without the touch the age reaches about 14 s). If 12 s flakes again, the fix needs a production config hook for `MessageTimeoutMs` or a design change, not another bound bump.
- Api start-up failure does not exit: fixed in 4a23e9b (`init: true`, start-up tests run the Api as a child process).
- 6.4 review (2026-10-30): move ReviewScreen.tsx:47 and WithdrawDialog.tsx:49 onto problems.ts validationFields, and keep one z.config({ jitless: true }) (linkSchema.ts:5, raiseSchema.ts:11). Refactor sweep.
- tf-2 review low (2026-10-30): the "fails again" focus tests (DisputeDetailScreen.test.tsx:266, DisputesScreen.test.tsx:428, TransactionsScreen.test.tsx:656) wait on `not.toHaveAttribute('disabled')`, which is always true (Try again uses aria-disabled); wait on a real settle signal (a second failure with traceId 'trace-2', then findByText(/trace-2/)) before asserting focus. Also for the refactor sweep: one retry() helper for the three screens. Test-only.

## Done 2026-10-30 (batch tf-2)

- Try again focus (WCAG 2.2 SC 2.4.3): DisputeDetailScreen focuses the h1 when a retry succeeds or returns 404 and leaves focus on Try again when it fails again; DisputesScreen and TransactionsScreen focus the h1 on success (Load more's Try again unchanged). DisputeDetailScreen is back to `else if (query.isError)` with no hoisted failure card.
- ReviewScreen shows MaskedNumber on its own line, so "Card" appears once.
- browserTelemetrySwitch.test.tsx stubs the unset case with `vi.stubEnv(name, undefined)`.
- browserTelemetrySwitch.test.tsx (6.4 fix round, 2026-10-30): one full Vitest run printed two 'Unhandled Errors: window is not defined' from this file (all tests passed; not seen in two later runs). Likely a timer or dynamic import resolving after the jsdom environment is torn down; find what outlives the test. Test-only.
