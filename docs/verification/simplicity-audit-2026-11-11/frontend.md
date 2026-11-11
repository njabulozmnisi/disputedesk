# Simplicity and standards audit: frontend (2026-11-11)

Scope: `frontend/src/` (tests included, `src/api/generated/` excluded), `vite.config.ts`, `orval.config.ts`,
`eslint.config.js`, `Dockerfile`, `package.json`. Read-only audit of the lows-2026-11-10 worktree
`.claude/worktrees/agent-a9958e0c9b5706eb6`: main at `f6193f5` plus that lane's uncommitted frontend change (35 modified
files, one new test). Its line numbers are cited as they stand there.

Rules: AGENTS.md "How to work", `docs/conventions/frontend.md`, `review-story` pass 2 and its standards check. The
severity scale and tags are the 2026-11-03 baseline's (`~/disputedesk-rename/retro-8/audit-rerun-plan.md`). Open frontend
rows in `docs/verification/lows.md` are not counted again; none of the findings below repeats one.

## Findings

| ID | Severity | Tag | File:line | Finding | Simplest fix |
|---|---|---|---|---|---|
| FRON-1 | low | [code] | `app/router.tsx:62`, `app/InactivityWarning.tsx:19`, `features/disputes/raise/SubmitFailure.tsx:53`, `features/disputes/detail/DisputeDetailScreen.tsx:110`, `features/disputes/withdraw/WithdrawDialog.tsx:37`, `features/disputes/history/DisputesScreen.tsx:22`, `features/transactions/TransactionsError.tsx:31`, `features/linking/DetailsScreen.tsx:117`, `features/linking/OtpScreen.tsx:183` | The copy deck's one unexpected-error sentence, "Something went wrong on our side. Try again.", is written 9 times: inline in 5 places and as constants with 4 different names (`unexpectedErrorMessage`, `unexpectedMessage`, `genericMessage`, `errorMessage`). This is well past the third-case rule, and a copy change has to find all nine. | Export one `unexpectedErrorMessage` beside `ProblemNotice` and use it in all nine places. |
| FRON-2 | low | [code] | `features/linking/DetailsScreen.tsx:34-35,79`; `features/linking/OtpScreen.tsx:82-83,142`; `features/disputes/raise/ReviewScreen.tsx:104-105,177` against `features/disputes/withdraw/WithdrawDialog.tsx:150-153,253` | Two mechanisms for one job, the synchronous double-press guard. Three screens keep an `attemptInFlight` ref that they set in the submit and clear in `onSettled`. The withdraw dialog reads `queryClient.isMutating({ mutationKey })` instead (TanStack Query, `QueryClient.isMutating`), which needs no ref and no clean-up. | Use one way everywhere. `isMutating` with the generated `get…MutationKey()` is the documented one and drops three refs and their `onSettled` resets. |
| FRON-3 | low | [code] | `features/disputes/raise/ReviewScreen.tsx:57-58,130` | `isDisputesQuery` matches the raw URL prefix `/api/v1/disputes` inside query keys. That breaks `frontend.md` ("Query keys come from the generated hooks") and ties the screen to how Orval builds a key. `WithdrawDialog.tsx:98` already invalidates the same list with `getListDisputesInfiniteQueryKey()`. | `queryClient.invalidateQueries({ queryKey: getListDisputesInfiniteQueryKey() })`, then delete the predicate. A new dispute has no cached detail query to invalidate. |
| FRON-4 | low | [code] | `components/ErrorSummary.tsx:10-11,17-28`; `features/disputes/raise/RaiseScreen.tsx:92-98,157` | Two owners focus the same element, and the outcome depends on the order React runs effects in. `ErrorSummary` focuses itself on each `requests` change. It also takes an optional `ref` that exists only so `RaiseScreen` can focus it a second time, after `usePageHeading`'s effect has moved focus to the h1. The comment at `RaiseScreen.tsx:93-95` is needed to explain this, which is a sign that it is not plain. | Keep one owner. On a screen that opens with a returned 400, let `usePageHeading` leave focus alone (`RaiseScreen` already knows `returnedErrors`). Then drop the `ref` option and the second effect. |
| FRON-5 | low | [code] | `api/queryClient.ts:64`, `app/RootLayout.tsx:58`, `app/Header.tsx:21` | The rule "signed in means `unlinked` or `linked`" is written out three times, which is the third real case. A fourth session state would have to be added in three files. | Compute `isSignedIn` once in `RootLayout` and pass it to `Header`, or export one `isSignedInState(state)` from beside the router's `homeOf` and use it in all three places. |
| FRON-6 | low | [code] | `components/ui/alert-dialog.tsx:100-115,137`; `test/support.tsx:171-186`; `features/disputes/detail/DisputeDetailScreen.test.tsx:871-880` | Dead code. `AlertDialogAction` is exported but never imported. The repository's own practice is to drop unused registry parts (`input-otp.tsx:14` drops `InputOTPSeparator`; `field.tsx:7` is "trimmed"). The shared test helper `recordWithdrawRequests` and its `WithdrawRequest` type have no caller, while the detail tests carry `serveWithdraw`, a line-for-line local copy of it. | Delete `AlertDialogAction`. Either use `recordWithdrawRequests` in the detail tests and delete `serveWithdraw`, or delete the shared helper. |
| FRON-7 | low | [code] | `passSeconds`: `features/linking/DetailsScreen.test.tsx:56`, `features/linking/OtpScreen.test.tsx:71`, `features/disputes/raise/raiseTelemetry.test.tsx:37`, `features/disputes/raise/ReviewScreen.test.tsx:43`. `location` stub: `app/signOut.test.tsx:154`, `app/InactivityWarning.test.tsx:41`, `api/queryClient.test.tsx:208,235,249,261`, `features/transactions/TransactionsScreen.test.tsx:421`, `features/disputes/raise/raiseTelemetry.test.tsx:185`, `features/disputes/raise/ReviewScreen.test.tsx:626`, against `app/sessionEnd.test.tsx:21-24` | Test helpers are copied, not shared. `passSeconds` (fake-timer advance inside `act`) is identical in four files. The jsdom `location` stub that records `replace` is written ten times across seven files, although `sessionEnd.test.tsx` already has it as `stubLocation`. `frontend.md` puts shared helpers in `src/test/`. | Move `passSeconds` and `stubLocation` into `src/test/support.tsx` and import them. |
| FRON-8 | low | [code] | `components/StateSurface.tsx:10-13,21,29`; `features/disputes/detail/DisputeDetailScreen.tsx:253-257,314-326` | `StateSurface` has a second mode for one caller. With no `icon`, it draws no surface and no tile and becomes a plain `<section>` styled by the caller's `className`. The detail screen uses that mode for its loading, failure and loaded states, so that its one h1 stays in one place in the tree. A plain `<section>` in the detail screen does the same, because React keeps the h1 in its slot when the icon tile beside it is `null`. As it stands, a "state surface" also renders content that is not a state. | Render the section in `DisputeDetailScreen` itself, with the not-found classes and the icon tile added only when not found. Make `icon` required on `StateSurface` and drop the no-icon branch. |

Standards check: no home-grown mechanism found. Session end is a document navigation (HTML navigate, `Location.replace`).
Gating is TanStack Router `beforeLoad` with `redirect` on pathless layout routes. Forms use React Hook Form with zod and
shadcn/ui `Field`. Problems are routed by RFC 9457 type. `Retry-After` follows RFC 9110 §10.2.3, with a `// shortcut:`
for the obsolete forms. Money is `bigint` cents, idle time is a wall-clock deadline (Page Visibility), and the `focus`
listener goes through TanStack Query's `focusManager.setEventListener`. Each one cites its source inline.

## Counts

| Severity | Baseline (2026-11-03) | Now (2026-11-11) |
|---|---|---|
| High | 1 | 0 |
| Medium | 2 | 0 |
| Low | 8 | 8 |
| Total | 11 | 8 |

Fate of the baseline findings, compared by kind of finding:

| Baseline | What it was | Now |
|---|---|---|
| FE-1 (high, [decision]) | Session data cleared in memory; late-response guards; route-tree patch and forced-GC tests | Closed: `app/sessionEnd.ts:27-31` leaves with `location.replace`; no `WeakSet`, write-back subscriber, guards or GC test remain |
| FE-2 (medium) | Reactive `SessionGate` component | Closed: `app/router.tsx:76-97`, pathless layouts with `beforeLoad` and `redirect` |
| FE-3 (medium) | Withdraw note outside React Hook Form and zod | Closed: `WithdrawDialog.tsx:42-75`, with `onError` in place of try/catch |
| FE-4 (low) | Inline error-summary copies; zod config twice | Closed: `ErrorSummary` used by all four forms; `src/zodConfig.ts` imported once |
| FE-5 (low) | Status checks and `errors` casts in place of problem types | Closed: `isProblem` and `validationFields` throughout; the one remaining status check is the bodiless 504 |
| FE-6 (low) | Session special case in the mutator | Closed: the special case is gone; the plain response listener stays, as the baseline allowed |
| FE-7 (low) | Client copy of `IsLocalUrl` | Closed: `router.tsx:165-174` keeps only the length bound |
| FE-8 (low) | Unused `FieldSet`/`FieldLegend`; home-made group label | Closed: `RaiseScreen.tsx:195-196`. The same kind (an unused registry export) returns as FRON-6 |
| FE-9 (low) | `formatToParts` lambda written three times | Closed: `format.ts:17` `partValue` |
| FE-10 (low) | `wide` prop with two meanings | Closed: `Header` takes `isStartPage` |
| FE-11 (low) | Convention said Orval mocks | Closed: `frontend.md` Tests line names MSW and `mock: false` |

## Trend

All 11 baseline findings are closed, including the one high and both mediums. The two root causes, in-place session
clean-up and the reactive gate, are now a document navigation and router `beforeLoad`. What is left is 8 new lows of a
smaller kind: repeated copy and rules past the third case (FRON-1, FRON-5), test helpers copied, not shared (FRON-7),
two ways to guard a double press (FRON-2), and one focus mechanism that depends on effect order (FRON-4). None of them
is a home-grown replacement for a standard, so the set has moved from structural debt to housekeeping, and the lows
batch can close all eight.
