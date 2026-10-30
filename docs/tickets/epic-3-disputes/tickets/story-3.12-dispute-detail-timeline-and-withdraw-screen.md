---
id: 12
type: story
title: "Dispute detail, timeline and withdraw screen"
parent: epic-3-disputes
covers: [R16, R17, R18, R19, R40]
after: [4, 7]
hitl: false
risk: medium
refined: false
---

# Dispute detail, timeline and withdraw screen

## Description

Adds screen 6, the dispute detail (`/disputes/<disputeId>`): the summary, the timeline oldest first, Withdraw while the dispute is open, the guidance after a Rejected outcome and the just-submitted banner after a raise. The withdraw dialog stays owned by the screen, so a status change mid-withdraw never strands it. It replaces the last plain `/disputes/<id>` anchors with typed links and keeps the dispute ID out of the sign-in `returnUrl` (AD-31). Split from story 3.7 (decided 2026-10-30); it reuses 3.7's outcome badges.

## Requirements

Verify: Vitest component tests pass, with API responses mocked by MSW from `openapi.json` shapes (stories 3.3 and 3.4's regenerated client); every new screen state passes the axe-core check the component tests already run; `npm run typecheck`, `npm run lint` and `npm test` pass in `frontend/` with no new suppression.

Inputs:
- Route parameter `disputeId`: accepted only as a canonical GUID, 36 characters, 8-4-4-4-12 hexadecimal with hyphens (`^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$`, RFC 9562 section 4); anything else is not found.
- Withdraw `note`: optional; at most 500 UTF-16 code units after trimming (`value.trim().length`); sent trimmed; empty or whitespace only means no note and the body is `{}`; input past the limit is allowed in the field and refused on confirm, never cut (FR-18; story 3.4, criterion 2).
- History state `justSubmitted`: a boolean set only by the raise screen's navigation after a 201.
- Response fields read: the `DisputeResponse` (story 3.3: `customerStatus`, `outcome`, `reason`, `disputedAmount`, `expectedAmount`, the `transaction` snapshot's description, date, amount, `maskedCard` and `accountLast4`) and its `timeline[]` (`action`, `customerStatus`, `actor`, `occurredAt`, `note`); a withdraw 409's `customerStatus` extension. Values outside the contract's enums render no label, never the raw value.

1. **The route takes only a GUID, and every not-found looks the same** (R17; FR-17, NFR-SEC-6)
   **Given** the route `/disputes/$disputeId` inside `SessionGate allow="linked"`
   **When** it is opened with `/disputes/abc`, `/disputes/..%2Fx%3Fy%23z`, `/disputes/<guid>%2F..`, a braced GUID, a GUID the API answers with 404 `not-found`, and an unknown route
   **Then** the route's `params.parse` accepts only the canonical GUID and throws `notFound()` before any query runs, because the router decodes path params and the generated client puts `disputeId` into the request path unencoded (TanStack Router, *Path params*, `params.parse`; OWASP ASVS 5.0 V1.2.2); the detail and withdraw hooks take only the parsed value
   **And** each case shows the one not-found screen ("We can't find that dispute", "Go to your disputes"), identical for the unknown GUID, another customer's dispute and the unknown route; for the malformed IDs MSW's `request:start` records no request under `/api/v1/disputes`

2. **Summary, states and Rejected guidance** (R16, R18; FR-17, NFR-API-5, NFR-CODE-6)
   **Given** a dispute detail loading, failing, loaded, or reached from a raise 201
   **When** the screen renders
   **Then** while loading the `h1` reads "Dispute" and the document title "Dispute · DisputeDesk", with a `Skeleton` header plus 3 timeline entries under `aria-busy="true"`; once loaded the `h1` is the snapshot's description and takes focus (`usePageHeading`; EXPERIENCE.md, State Patterns)
   **And** the query's error is checked before its data, so a failed refetch never leaves a stale detail on screen: a 404 shows the not-found screen; any other error (5xx, network, another 4xx) shows the problem notice "Something went wrong on our side. Try again." with the `traceId` and "Try again" (refetch); the problem's `title` and `detail` are never shown (AD-3)
   **And** loaded, the summary shows the status badge (3.7's outcome badges), the disputed amount, the reason label, the transaction date, the masked card or account number (`MaskedNumber`) and, only when `expectedAmount` is not null, "Transaction amount" and "Expected amount" through `formatMoney`, plus a "Back to disputes" `Link` (DESIGN.md, Dispute summary card; `dispute-rejected.html`); an unknown `reason` renders no label, never the raw value; no dispute reference row is shown
   **And** when the history entry's state has `justSubmitted` (set by the raise screen's `navigate` with `state`, typed through TanStack Router's `HistoryState` interface), a static banner "Dispute submitted" shows above the summary; it is not a live region, because the raise screen already announces "Dispute submitted"; it shows again on that history entry after a reload or Back and Forward, which is accepted; the signal is never a query parameter or storage (pre-build security S4)
   **And** only when `outcome` is `rejected`, the guidance shows exactly as EXPERIENCE.md words it: "If you disagree with this outcome, you can complain to the bank first. If you're still not satisfied after the bank has answered your complaint, you can take it to the National Financial Ombud Scheme South Africa, an independent service that is free for customers." (FR-17)

3. **Timeline as the customer sees it** (R16, R18; FR-17, NFR-API-5)
   **Given** a timeline with one entry per action
   **When** it renders
   **Then** it is an `<ol>` in API order (oldest first); each entry shows the status label (the resolve entry uses the dispute's outcome badge label), a line keyed by `action`, with the resolve line keyed by the dispute's `outcome`: `raise` "You submitted this dispute.", `startReview` "The bank started reviewing your dispute.", `escalate` "The bank prioritised the review of your dispute.", `resolve` with `upheld` "The bank agreed with you.", `resolve` with `rejected` "The bank didn't uphold your dispute.", `withdraw` "You withdrew this dispute." (EXPERIENCE.md, Copy deck); the actor `you` as "You" and `theBank` as "The bank"; the note in quotes as React text, with no autolinking
   **And** the time reads like "26 Oct 2026, 14:32 SAST": the parts come from `Intl.DateTimeFormat('en-US', …).formatToParts` with `day`, `month: 'short'`, `year`, `hour`, `minute`, `hourCycle: 'h23'` and `timeZone: 'Africa/Johannesburg'`, put in that order, plus the literal " SAST" (Africa/Johannesburg has no daylight saving, AD-4), as `format.ts` already does for the day; it sits inside `<time dateTime={occurredAt}>` holding the API's UTC instant unchanged; tests pin one October and one September instant to their exact text (September reads "Sep", never "Sept")
   **And** an open dispute ends the timeline with "We'll update the timeline as your dispute moves."; an unknown `action`, `customerStatus`, `actor` or `outcome` renders no line or label, never the raw value or "Escalated", each mapping an exhaustive `switch` whose `default` assigns to a read `never` variable (AD-13; story 3.10, T2)

4. **Withdraw offered only while open, in an accessible dialog** (R19, R40; FR-18, FR-20, NFR-CODE-6)
   **Given** disputes in each `customerStatus`
   **When** the detail renders
   **Then** the secondary button "Withdraw dispute" shows for `submitted`, `underReview` and `underReviewPrioritised` only, never for `resolved`, `withdrawn` or an unknown value
   **And** it opens a shadcn/ui `AlertDialog` (WAI-ARIA APG alert dialog pattern), found in tests as `getByRole('alertdialog', { name: 'Withdraw this dispute?' })`, with "You can't dispute this transaction again after you withdraw.", the optional note, "Withdraw dispute" (destructive) and "Keep dispute"; the detail screen owns its `open` state, so the dialog stays mounted when a refetch removes the Withdraw button
   **And** focus starts on "Keep dispute", the least destructive action, which is Radix `AlertDialog`'s default (WAI-ARIA APG, *Dialog (Modal)*, initial focus for irreversible actions) and stays inside; when not pending, Escape or "Keep dispute" closes it and focus returns to "Withdraw dispute"; while a request is pending, Escape does nothing (`onEscapeKeyDown` with `preventDefault`) and "Keep dispute" has `aria-disabled` and does nothing, tested with a delayed response

5. **The note and the pending confirm** (R19; FR-18, NFR-CODE-6)
   **Given** the dialog's note, labelled "Note (optional)"
   **When** the customer types, pastes and confirms
   **Then** a live count "{n} / 500" counts after trimming and is announced politely only at 450 and at 500, as 4a's note does (GOV.UK Design System, *Character count*); 500 code units after trimming, a 500-unit note padded with spaces and whitespace only are accepted, and 501 shows "Your note must be 500 characters or fewer." under the field and in an error summary that takes focus, with no request (EXPERIENCE.md, Field errors)
   **And** confirm sends one request through the generated withdraw mutation with `retry: false`; the body is `{ "note": <the trimmed note> }`, or `{}` for an empty or whitespace-only note, as `raiseSchema.ts` trims; the test asserts the request carries `X-XSRF-TOKEN`, carries no `Idempotency-Key`, and has no note in its URL (pre-build security S6); while pending the button reads "Withdrawing…", has `aria-disabled`, and a second press sends nothing, blocked synchronously as `ReviewScreen.tsx` does

6. **Withdraw outcomes** (R19; FR-18, FR-19, FR-20, FR-24)
   **Given** the confirm's response
   **When** it is 200, or 409 `action-not-allowed` with `customerStatus` `withdrawn`
   **Then** on 200 the response body is written into the detail cache with `setQueryData` (TanStack Query, *Updates from Mutation Responses*), and the disputes list and transactions queries are invalidated (TanStack Query, *Query invalidation*); on the 409 the detail's `invalidateQueries` is awaited before the dialog closes, so a retried withdraw shows its result rather than implying its note was saved (FR-18)
   **And** the dialog closes; "Dispute withdrawn" is announced through the one `useAnnounce` live region, with no toast package (pre-build security S3); focus moves to the `h1` through Radix `onCloseAutoFocus` with `preventDefault()`, because the Withdraw button is gone (WCAG 2.2 SC 2.4.3); one test asserts together that the badge reads "Withdrawn", the button is absent and focus is on the `h1`, with the detail GET answering 500 after the POST
   **And** the other responses, the dialog stays open unless stated:
   - 409 `action-not-allowed` with `customerStatus` `resolved`: the notice "This dispute is now Resolved. It can't be withdrawn." and the detail is invalidated; the notice stays readable after the refetch removes the Withdraw button;
   - 409 `action-not-allowed` with an open `customerStatus` (a second version clash, FR-24, where a retry can succeed): "Something went wrong on our side. Try again." with the `traceId`, the note kept, the detail invalidated;
   - 409 with an unknown `customerStatus`: "This dispute can't be withdrawn any more.";
   - 400 `validation` naming `note`: the note error and summary;
   - 404: the dialog closes and the not-found screen shows;
   - 5xx, network, or any other error (another 4xx, a 400 not naming `note`): "Something went wrong on our side. Try again." with the `traceId`, the note kept
   **And** on any later close, focus goes to "Withdraw dispute" if it is still shown, else to the `h1` (`onCloseAutoFocus`); `problems.ts` gains the `action-not-allowed` slug, and `problemExtension` reads `customerStatus`

7. **Typed links in, no dispute ID in `returnUrl`** (R17, R40; AD-31)
   **Given** the plain `/disputes/<id>` anchors in `SubmitFailure.tsx` ("View dispute") and `TransactionRow.tsx` (story 3.10's badge), and the raise screen's `navigate({ href })` after a 201
   **When** the route exists
   **Then** each becomes a typed `Link` or `navigate({ to: '/disputes/$disputeId', params, replace: true, state: { justSubmitted: true } })`, its `// shortcut:` comment removed; the badge's `href` stays `/disputes/<guid>`
   **And** after a 401 on any `/disputes/<…>` path (the detail load and the withdraw POST), `returnUrlAfterSignIn` gives `/disputes`, so `/signed-out?returnUrl=` holds `%2Fdisputes` and no GUID; a 401 on `/disputes` keeps `/disputes`; the raise rule (`/transactions`) is unchanged (AD-31; OWASP ASVS 5.0 V14.2.1)
   **And** the shared `returnUrlAfterSignIn` compares the lower-cased pathname, because TanStack Router matches routes case-insensitively by default: `/Disputes/<guid>` gives `/disputes` and `/Transactions/<id>` gives `/transactions`, each with no identifier

## Boundaries

- Must not change: the raise flow's behaviour and copy (story 3.6) beyond the typed navigation and its state; the history screen (story 3.7); the API (stories 3.3, 3.4).
- No dispute data in `localStorage`, `sessionStorage` or a TanStack Query persister; a detail test asserts both storages are empty, as `ReviewScreen.test.tsx` does (pre-build security S4).
- Not here: the backend's 400 body for a non-GUID route ID (5.1's note below): the SPA no longer sends one.

## Supervisor checks

Deliberate-break proofs, run by the supervisor, not the verifier.

- In a throwaway copy, drop the GUID check from `params.parse`: criterion 1's no-request test fails.
- In a throwaway copy, show Withdraw for `resolved`: criterion 4's test fails.
- In a throwaway copy, return `location.href` from `returnUrlAfterSignIn` for `/disputes/<id>`: criterion 7's test fails.
- In a throwaway copy, render the dialog inside the Withdraw button's open-status condition: criterion 6's `resolved` notice test fails.

## Build facts

- Owner: frontend (frontend-builder).
- Flags: none.
- Lane: B.
- Mechanisms and sources: TanStack Router `params.parse`, `notFound()`, `Link`, `navigate` with `state` and the `HistoryState` interface (<https://tanstack.com/router/latest/docs/framework/react/guide/path-params>, <https://tanstack.com/router/latest/docs/framework/react/guide/not-found-errors>, <https://tanstack.com/router/latest/docs/framework/react/guide/navigation>); Orval-generated detail query and withdraw mutation; TanStack Query invalidation and updates from mutation responses (<https://tanstack.com/query/latest/docs/framework/react/guides/query-invalidation>, <https://tanstack.com/query/latest/docs/framework/react/guides/updates-from-mutation-responses>); shadcn/ui `AlertDialog` on Radix with `onCloseAutoFocus` and `onEscapeKeyDown` (<https://www.radix-ui.com/primitives/docs/components/alert-dialog>), WAI-ARIA APG alert dialog and modal dialog initial focus (<https://www.w3.org/WAI/ARIA/apg/patterns/alertdialog/>, <https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/>); WCAG 2.2 SC 2.4.3 focus order; GOV.UK character count and error summary (<https://design-system.service.gov.uk/components/character-count/>, <https://design-system.service.gov.uk/components/error-summary/>); `Intl.DateTimeFormat` (<https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Intl/DateTimeFormat>); RFC 9562 UUID text form; the one polite live region `useAnnounce` (story 2.4).
- Packages: none (no toast library).
- Touches:
  - `frontend/src/features/disputes/detail/` and `withdraw/` (new: the screen, timeline, dialog, their tests)
  - `frontend/src/app/router.tsx` (the `/disputes/$disputeId` route and the `HistoryState` augmentation)
  - `frontend/src/api/queryClient.ts` and `queryClient.test.tsx` (`returnUrlAfterSignIn`: `/disputes` from a detail path, and the lower-cased pathname compare shared with the raise rule)
  - `frontend/src/api/problems.ts` and `problems.test.ts` (`action-not-allowed`, `customerStatus`)
  - `frontend/src/features/disputes/raise/SubmitFailure.tsx`, `ReviewScreen.tsx`, `ReviewScreen.test.tsx` (typed link and navigation; tests that expected the not-found screen after a 201 now expect the detail)
  - `frontend/src/features/transactions/TransactionRow.tsx`, `TransactionsScreen.test.tsx` (the badge as a typed `Link`; `MaskedNumber`'s prop narrows to `Pick<TransactionResponse, 'maskedCard' | 'accountLast4'>` so the snapshot can use it)
  - `frontend/src/features/transactions/format.ts` and `format.test.ts` (the timeline time)
  - `frontend/src/app/routing.test.tsx` (`/disputes/abc` stays not-found, now through `params.parse`)
  - `frontend/src/test/support.tsx` (`makeDispute` with a GUID `disputeId` and timeline entries; detail and withdraw MSW helpers) and `frontend/src/test/raise.tsx` (the `Location` header and created dispute's GUID)
  - `e2e/tests/dispute-raise.spec.ts` (QA stage: after a 201 the detail shows, not the not-found screen)
  - `e2e/tests/spa-dispute-status-badges.spec.ts` (QA stage: following the badge shows the detail, not the not-found screen; the badge `href` assertions are re-checked against the typed `Link`)
  - `e2e/tests/dispute-detail.spec.ts` (QA stage: the header comment's "the detail screen is story 3.7" becomes 3.12)
  - QA stage: a 1280px screenshot of the detail shows summary and timeline in two columns (EXPERIENCE.md, Responsive); jsdom cannot check layout, so Vitest does not
- Reuses, not rewrites: 3.7's badge mapping, `MaskedNumber`, `ProblemNotice`, `NotFoundPage`, 4a's note count and error-summary pattern (plain duplication is fine for a second use).

## References

- parent — docs/tickets/epic-3-disputes/epic-3-disputes.md, section Requirements
- design — docs/ux/DESIGN.md, Components, Dispute summary card
- design — docs/ux/mockups/dispute-submitted.html
- design — docs/ux/mockups/dispute-under-review.html
- design — docs/ux/mockups/dispute-prioritised.html
- design — docs/ux/mockups/dispute-upheld.html
- design — docs/ux/mockups/dispute-rejected.html
- design — docs/ux/mockups/dispute-withdrawn.html
- design — docs/ux/mockups/dispute-withdraw.html
- design — docs/ux/mockups/dispute-loading.html
- design — docs/ux/mockups/not-found.html
- experience — docs/ux/EXPERIENCE.md, Copy deck, Component Patterns (Timeline, Withdraw, Note field), State Patterns, Accessibility Floor
- architecture — docs/architecture/architecture.md, AD-3, AD-13, AD-31
- prd — docs/prd/prd.md, FR-17, FR-18, FR-19, FR-20, FR-24
- verification — docs/verification/3.7-challenge.md, docs/verification/3.7-prebuild-review.md, docs/verification/3.7-prebuild-security.md, docs/verification/3.12-challenge.md, docs/verification/3.12-prebuild-review.md

## Notes

- Risk medium (proposed): Withdraw is final (FR-19), so a control shown in the wrong status or a wrong success message misleads a customer about an irreversible action; beyond the criteria, the supervisor's Withdraw-for-`resolved` break proves the test.
- Decision: 2026-10-30 (Sofia, refine) — split from 3.7; it waits on 3.4 (the withdraw operation and regenerated client) and 3.7 (the badges, the `/disputes` route and the shared files `router.tsx` and `test/support.tsx`).
- Decision: 2026-10-30 (Sofia, refine, pre-build security S3) — "Dispute withdrawn" is announced through the existing live region; no toast package, because a toast library that injects a `<style>` is blocked by the gateway's `style-src 'self'`. EXPERIENCE.md already says so.
- Decision: 2026-10-30 (Sofia, refine, 3.12 pre-build review) — no dispute reference row. FR-17 asks for no visible dispute reference, the reference a customer quotes to the bank is the problem notice's `traceId`, and the mockups' 8-character form was a truncated GUID that no bank tool can look up. The row is removed from DESIGN.md (Dispute summary card, Mono) and the six detail mockups.
- Decision: 2026-10-30 (Sofia, refine, 3.12 challenge and pre-build review) — the dialog's initial focus is "Keep dispute", Radix `AlertDialog`'s default, because withdrawing is irreversible and the WAI-ARIA APG modal dialog guidance puts initial focus on the least destructive action; `dispute-withdraw.html`'s note is changed to match.
- Decision: 2026-10-30 (Sofia, refine, 3.12 pre-build review) — the just-submitted banner sits above the summary, as `dispute-submitted.html` shows; EXPERIENCE.md's "above the timeline" is changed to match, and the mockup's `role="status"` is removed because the raise screen already announces it.
- Decision: 2026-10-30 (Sofia, refine, 3.12 pre-build review) — EXPERIENCE.md's "Sign in again returns to the same route" now names AD-31's exception: a raise or detail route returns to its list.
- Carried from 3.6 (2026-10-30, AD-31): after a 401 on the dispute detail, `returnUrl` carries no dispute ID; criterion 7. The case-insensitive compare also closes the same gap in 3.6's raise rule.
- Carried from 2.4's review: an exhaustiveness check is an assignment to a `never` variable that is also read, so `noUnusedLocals` and ESLint accept it with no suppression; criterion 3.
- Carried from 5.1's review (2026-10-30): the operations read answers a non-GUID `disputeId` with a 400 that names the field, while the customer detail's 400 has no `errors`. This story is frontend only and the SPA no longer sends a non-GUID, so nothing changes here; abstract the endpoint filter when a third route-GUID endpoint needs it.
