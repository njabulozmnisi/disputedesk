---
id: 3
type: story
title: "Frontend tidy: route guards in beforeLoad, and one form pattern"
parent: epic-8-simplicity-sweep
covers: [R2, R9, R34, R40]
after: []
hitl: false
risk: medium
refined: true
---

# Frontend tidy: route guards in beforeLoad, and one form pattern

## Description

Slice S2 of the mitigation plan (wave 2). The SPA's session routing wraps each screen in `<SessionGate>`, a component that redirects whenever the cached session changes, not only on navigation; TanStack Router's documented approach is a `beforeLoad` that throws `redirect` on a pathless layout route (FE-2). The withdraw note is a form built from five pieces of React state where AD-31 prescribes React Hook Form with zod (FE-3). Smaller drift: two inline copies of the error summary and two `z.config` calls (FE-4), problem bodies read three ways and status checks where problem types exist (FE-5), a client copy of the BFF's `returnUrl` rule (FE-7), unused `FieldSet`/`FieldLegend` beside a hand-made radio-group label (FE-8), three copies of one formatter lambda (FE-9) and a header prop named `wide` that also hides the Sign in button (FE-10). What the customer sees and hears is unchanged.

## Requirements

Verify: `npm run typecheck`, `npm run lint` and `npm test` pass in `frontend/` with no new suppression; the routing, raise, withdraw and linking tests pass, changed only where a criterion names the change; the e2e regression suite passes on the stack (QA stage).

Inputs:
- `returnUrl` search parameter of `/signed-out`: kept when it is a string of 1 to 2048 characters, otherwise `undefined`; it only ever becomes the `returnUrl` query of the same-origin `GET /api/v1/bff/login` link, where the BFF accepts at most 2048 characters and only a local URL (`IsLocalUrl`, AD-7).
- `reason` search parameter of `/signed-out`: unchanged (only `expired`).
- Withdraw note: unchanged bound, at most 500 characters after trimming.

1. **Session states are guarded in `beforeLoad` on pathless layout routes** (FE-2; R9, AD-31)
   **Given** the six screens wrapped in `<SessionGate>` in `src/app/router.tsx:33-103`, the seventh use in `RaiseLayout.tsx:73`, and `src/app/SessionGate.tsx`
   **When** this story lands
   **Then** the root route is created with `createRootRouteWithContext<{ queryClient: QueryClient }>()`, and three pathless layout routes (`_anonymous`, `_unlinked`, `_linked`) each run `beforeLoad`, which reads the session with `context.queryClient.ensureQueryData` on the generated session query's options and throws `redirect({ to: homeOf[session.state], replace: true })` when the state is not the route's own (`homeOf` as in `SessionGate.tsx:12-16`); the raise route and its two children sit under `_linked`
   **And** `src/main.tsx` creates the query client first, passing `createQueryClient` a function that returns the router (its error handlers and session subscription read the router only when they run, never while it is built), then creates the router with `context: { queryClient }`; the test support's app factory does the same; what the handlers do is unchanged
   **And** each layout route sets `pendingMs: 0` and `pendingMinMs: 0` with a `pendingComponent` that renders today's `<div aria-busy="true" />`, so the busy state shows at once and leaves as soon as the session is known, as `SessionGate` does
   **And** a failed session request renders the layout route's `errorComponent` with today's problem notice and "Try again", whose click calls `router.invalidate()` so the guard runs again; `SessionGate.tsx` is deleted
   **And** `/signed-out` and the not-found route stay children of the root, outside the three layout routes, and the root route has no `beforeLoad` that reads the session, so neither page is redirected by a session state
   **And** `RaiseLayout`'s `RaiseGuard` is unchanged: it still reads the cached row once into state, so a cache collection mid-flow does not send the customer away, and still checks the submitted draft; only its `SessionGate` wrapper goes

2. **The withdraw note uses React Hook Form, zod and Field** (FE-3; R34, AD-31)
   **Given** `src/features/disputes/withdraw/WithdrawDialog.tsx:54-176`
   **When** this story lands
   **Then** the note is `useForm` with `zodResolver(z.object({ note: z.string().refine((note) => note.trim().length <= 500, …) }))`, rendered with `Controller` and `Field`, and a server 400 naming the note is set with `setError('note', …)`, as `RaiseScreen` does; the request body still sends the trimmed note, the dialog stays locked while the request is pending, and the 404 and 409 handling is unchanged
   **And** the mutation's `onSuccess`, `onError` and `isPending` replace `attempted`, `serverRefusedNote`, the `attemptInFlight` ref, the lifted `busy` flag and the `try`/`catch`/`finally`; the `instanceof Error` wrap into a new `TypeError` (`WithdrawDialog.tsx:171`) goes, because the hook's error type is `ApiError | TypeError` (`src/api/mutator.ts:28`)
   **And** the withdraw dialog tests pass unchanged: focus, the summary, the announced character level, the 404 and 409 notices

3. **One error summary and one zod configuration** (FE-4; R34, R40)
   **Given** the inline summaries in `RaiseScreen.tsx:137-168` and `WithdrawDialog.tsx:187-215` beside `src/components/ErrorSummary.tsx`, and `z.config({ jitless: true })` in `features/linking/linkSchema.ts:5` and `features/disputes/raise/raiseSchema.ts:11`
   **When** this story lands
   **Then** both screens render `ErrorSummary`, which gains one optional form-level line for RaiseScreen's form error, with the same markup, focus and links as today (the axe and summary tests pass unchanged)
   **And** `z.config({ jitless: true })` is called once, in a side-effect module `src/zodConfig.ts` whose comment keeps today's reason (the CSP has no `'unsafe-eval'`), imported first in `src/main.tsx` and in the Vitest setup file `src/test/setup.ts`, so it runs before any schema is constructed (zod reads the setting when a schema is built, `zod/v4/core/schemas.js:1194`); the two schema files no longer call it

4. **Problem bodies are read by type** (FE-5; AD-3, AD-31)
   **Given** the `as { errors?: … }` casts and `status === 400` or `404` checks (`ReviewScreen.tsx:48`, `WithdrawDialog.tsx:49-52,117,121`, `DisputeDetailScreen.tsx:194,236`)
   **When** this story lands
   **Then** each uses `validationFields(error)`, `isProblem(error, 'validation')` or `isProblem(error, 'not-found')` from `src/api/problems.ts`; a status check stays only for a bodiless gateway response (504 in `SubmitFailure.tsx:20`)

5. **The SPA leaves the `returnUrl` rule to the BFF** (FE-7; R2, AD-7)
   **Given** `localPathOrUndefined` (`router.tsx:122-136`) on `/signed-out`'s `validateSearch` (`router.tsx:142-145`)
   **When** this story lands
   **Then** `validateSearch` keeps `returnUrl` when it is a string of 1 to 2048 characters and drops it otherwise; the single-slash rule and the WHATWG parse go; the length cap stays, because the BFF refuses a longer value with 400 (`[MaxLength(2048)]`) where today the customer gets a plain sign-in link
   **And** in `src/app/routing.test.tsx:202-247` the `it.each` of 13 non-local values and the raw `%09` case are inverted: each value now reaches the "Sign in again" link as its encoded `returnUrl` query (only `''` still gives the plain link), and the over-2048 case stays unchanged
   **And** the BFF integration test `Callback_NonLocalReturnUrl_RedirectsToRoot` (`backend/tests/DisputeDesk.Api.IntegrationTests/Bff/SignInTests.cs:190-195`) proves the BFF refuses the non-local values (OWASP, *Unvalidated Redirects and Forwards*: validate on the server), and gains the cases `"/\t/evil.example"` and `"/\r\n/evil.example"` (a tab, and a carriage return and line feed, after the first slash), which the SPA's WHATWG parse caught before this story; both redirect to `/`

6. **The reason radio group is a fieldset with a legend** (FE-8; R40)
   **Given** `RaiseScreen.tsx:185-201` (`<p id="reason-label">` with `aria-labelledby`) and the unused `FieldSet`, `FieldLegend` exports (`src/components/ui/field.tsx:6,9-21,63`)
   **When** this story lands
   **Then** the reason group is a `FieldSet` whose `FieldLegend` reads as today's label, so a native `fieldset` and `legend` name the group; the accessible name the tests query is unchanged and axe passes

7. **One formatter helper and a prop named for what it does** (FE-9, FE-10; R34)
   **Given** the three `part` lambdas in `src/features/transactions/format.ts:20,39,56` and the header's `wide` prop (`src/app/Header.tsx:12-13,18,26,46`, passed at `src/app/RootLayout.tsx:77`)
   **When** this story lands
   **Then** the three formatters call one `partValue(parts, type)`, and the header's prop is `isStartPage`; the `Footer`'s own `wide` prop (`RootLayout.tsx:18,24,92`) stays, because it sets only the width; the format and header tests pass unchanged

## Boundaries

- Must not change: copy, routes and their URLs, focus order, the session query's options, what the query client's error handling does, `src/app/useSessionTabs.ts` (8.13 replaces it), the mutator, any generated file. `src/app/sessionEnd.ts` changes only in its comment (below).
- A session-state change with no navigation no longer redirects by itself. Today every writer of the session cache that changes its state also navigates (`queryClient.ts:27-30` to /link, `useGoToTransactions.ts:18-21` to /transactions, `sessionEnd.ts` to /signed-out before it writes `anonymous`), and the session query never refetches on focus (`queryClient.ts:44`, `staleTime: Infinity`), so no screen changes. 8.13's focus refetch (D2) ends the session by a document navigation itself.
- The comments that explain an order by `SessionGate` (`sessionEnd.ts:101-112`, `queryClient.ts:54-56`, `useGoToTransactions.ts:10`) are corrected to name the layout routes' `beforeLoad`; nothing else in those files changes.
- `src/test/support.tsx`'s `holdSessionEndedRoute` (`:287-320`) stays: it patches only `/signed-out`'s own `beforeLoad`, which keeps its place outside the layout routes.
- No new package (TanStack Router 1.170.46, TanStack Query 5.105.0, React Hook Form 7.91.0 and zod 4.6.7 are already pinned).

## Supervisor checks

- Throwaway copy: in `_linked`'s `beforeLoad`, redirect only for `anonymous`; the routing test for an unlinked login on `/transactions` fails.
- Throwaway copy: make "Try again" do nothing; the failed-session test (criterion 1) fails.
- Throwaway copy: change the withdraw schema bound to 499; the 500-character note test fails.
- Throwaway copy: remove the `FieldLegend`; the accessible-name test of the reason group fails.
- Throwaway copy: move the `zodConfig` import below the router import in `main.tsx`; the CSP e2e check of the raise form (QA stage) fails with an `unsafe-eval` violation.

## Build facts

- Owner: frontend (frontend-builder).
- Tier: Feature (FE-2 touches routing; FE-7 touches a redirect input, so the security agent reads the diff).
- Flags: none.
- Lane: B.
- Mechanisms and sources:
  - Authenticated routes with `beforeLoad` and `redirect`: <https://tanstack.com/router/latest/docs/framework/react/guide/authenticated-routes>.
  - Router context with TanStack Query: <https://tanstack.com/router/latest/docs/framework/react/guide/router-context>.
  - Pending and error components, `pendingMs`, `pendingMinMs`, `router.invalidate()` to retry: <https://tanstack.com/router/latest/docs/framework/react/guide/data-loading>.
  - Forms with React Hook Form and zod: <https://ui.shadcn.com/docs/forms/react-hook-form>.
  - Radio group and grouping controls: <https://ui.shadcn.com/docs/components/radio-group>; <https://www.w3.org/WAI/tutorials/forms/grouping/>.
  - Server-side redirect validation: <https://cheatsheetseries.owasp.org/cheatsheets/Unvalidated_Redirects_and_Forwards_Cheat_Sheet.html>.
  - zod global configuration `z.config` and `jitless`: the zod 4 core source, as `raiseSchema.ts:9` cites it (`core/schemas.js:1194` reads the setting); the builder adds the zod documentation page that describes `z.config` (<https://zod.dev/>), which this story did not verify.
- Packages: none.
- Touches: `src/app/router.tsx`, `src/app/SessionGate.tsx` (deleted), `src/main.tsx`, `src/zodConfig.ts` (new), `src/test/setup.ts`, `src/test/support.tsx` (the app factory's bootstrap), `src/api/queryClient.ts` (the router function and a comment), `src/app/sessionEnd.ts` and `src/features/linking/useGoToTransactions.ts` (comments), `src/features/disputes/raise/RaiseLayout.tsx`, `RaiseScreen.tsx`, `ReviewScreen.tsx`, `raiseSchema.ts`, `src/features/linking/linkSchema.ts`, `src/features/disputes/withdraw/WithdrawDialog.tsx`, `src/features/disputes/detail/DisputeDetailScreen.tsx`, `src/components/ErrorSummary.tsx`, `src/features/transactions/format.ts`, `src/app/Header.tsx`, `src/app/RootLayout.tsx`, and their tests (`src/app/routing.test.tsx` as criterion 5 says).
- The two `InlineData` cases in `SignInTests.cs` (criterion 5) are outside the frontend builder's paths: the supervisor dispatches them to a backend builder as a test-only change before 8.3 merges (human decision, 2026-11-04); if either case is not redirected to `/`, that is a security finding and 8.3 waits on it.
- Collision: 8.13 changes `router.tsx` (the signed-out route), `RootLayout.tsx`, `WithdrawDialog.tsx`, `DisputeDetailScreen.tsx`, `sessionEnd.ts` and `queryClient.ts` after this story merges.

## Pre-build

- Challenger: `docs/verification/8.3-challenge.md` (simplify).
- Review and security: `docs/verification/epic-8-prebuild-wave2.md`, section 8.3 (partial); `docs/verification/epic-8-prebuild-wave2b.md`, section 8.3 (the rest); `docs/verification/epic-8-prebuild-wave3.md` (security, two points, folded earlier).
- Decisions: none raised as `[decision]`; the human decided on 2026-11-04 that the two backend `returnUrl` cases go to a backend builder as a test-only change (`docs/verification/epic-8-decisions.md`, Build facts).
- Sofia, 2026-11-04: every `[story]` point folded. Review: the `SessionGate` comments corrected, `holdSessionEndedRoute` stays (Boundaries); `zodConfig.ts` imported first in `main.tsx` and the Vitest setup, with the zod docs cited (criterion 3); `pendingMs: 0` (criterion 1); a session change without navigation stated, with why no screen changes (Boundaries); the routing tests inverted named (criterion 5); `field.tsx:63` and the Footer's `wide` (criteria 6, 7). Challenger: "Try again" reruns the guard by `router.invalidate()` (criterion 1, Supervisor checks); the over-2048 value stays dropped (criterion 5, Inputs); the bootstrap order (criterion 1); the raise row stays captured once in `RaiseLayout` (criterion 1); the trimmed body, pending lock and 409 kept (criterion 2); tests import the zod setting (criterion 3). Security (earlier run): the control-character cases on the BFF test (criterion 5); `/signed-out` and not-found outside the layouts (criterion 1).

## References

- parent — docs/tickets/epic-8-simplicity-sweep/epic-8-simplicity-sweep.md, section Requirements
- audit — docs/verification/simplicity-audit/frontend.md, FE-2 to FE-5, FE-7 to FE-10
- plan — docs/verification/simplicity-audit/mitigation-plan.md, wave 2, S2
- architecture — docs/architecture/architecture.md, AD-3, AD-7, AD-31
- conventions — docs/conventions/frontend.md

## Notes

- Risk medium (proposed): routing guards every screen; the routing tests pin each state's redirect.
- Decision: 2026-11-04 (Sofia) — FE-8 labels the group with `FieldSet` and `FieldLegend` rather than deleting them, because a native `fieldset` and `legend` is the WAI grouping pattern.
- Decision: 2026-11-04 (Sofia) — FE-7 keeps one rule, the BFF's; no client copy is kept as defence in depth, as the audit's alternative allows only with a stated reason and none is recorded. The 2048-character cap is kept as an input bound, not a redirect rule: without it an over-long value gets the BFF's 400 instead of today's plain sign-in link.
- Sofia, 2026-11-04: the two security `[story]` points for 8.3 in `docs/verification/epic-8-prebuild-wave3.md` are folded: the control-character cases on the BFF test (criterion 5, Build facts) and `/signed-out` and not-found outside the layout routes with no root `beforeLoad` (criterion 1).
- Decision: 2026-11-04 (Sofia) — FE-6 (the mutator's session special case) is not here: it goes with D2 in 8.13.
- Decision: 2026-11-04 (Sofia) — `pendingMs: 0` and `pendingMinMs: 0` on the layout routes, because the router's defaults (1000 ms and 500 ms in router-core) would show nothing for a second and then hold the busy state for half a second, where `SessionGate` shows it at once and only while the session is unknown.
- Decision: 2026-11-04 (Sofia) — no `router.invalidate()` on a session-state change: every state change today already navigates (Boundaries), so a subscription would add code for no screen change.
- Decision: 2026-11-04 (Sofia) — the raise route's cached-row check stays in `RaiseLayout`, not `beforeLoad`: it is not a session state, and a `beforeLoad` runs again on the form-to-review navigation, where a collected cache entry would send the customer away mid-flow.
- Decision: 2026-11-04 (Sofia) — the router reaches the query client through `createQueryClient(() => router)`, because the client's error handlers need the router only when an error arrives; it needs no `!` and no second context source.
