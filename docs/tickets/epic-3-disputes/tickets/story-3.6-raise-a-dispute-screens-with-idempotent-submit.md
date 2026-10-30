---
id: 6
type: story
title: "Raise-a-dispute screens with idempotent submit"
parent: epic-3-disputes
covers: [R12, R13, R14, R15, R40]
after: [2.4, 2.5, 2, 3]
hitl: false
risk: medium
---

# Raise-a-dispute screens with idempotent submit

## Description

Adds the raise and review screens (4a, 4b): reason, note and expected amount on a shadcn/ui `Field` form, one frozen request and one `Idempotency-Key` per draft, and every submit outcome routed by problem type. It is the SPA half of raising a dispute; story 10 adds the dispute badges on transaction rows and story 11 the browser spans for SM-1 and SM-2. It consumes the generated client that story 3.3 regenerates and does not edit it.

## Requirements

Verify: Vitest component tests pass, with API responses mocked by MSW from `openapi.json` shapes; `npm run typecheck`, `npm run lint` and `npm test` pass in `frontend/`.

1. **The raise screens open only for a cached, disputable row** (R12, R13; FR-12, FR-13)
   **Given** the route `/transactions/<transactionId>/dispute` or `/dispute/review`
   **When** it opens
   **Then** it takes the row from the cached pages of the transactions list query (there is no GET for one transaction), and the screen shows that row's description, date, masked number and amount
   **And** when no cached row has that `transactionId` (a deep link, a reload) or the row is not disputable, the route redirects to `/transactions` with `replace` (a TanStack Router `<Navigate replace>` rendered inside the session gate, once the session check has passed); 4b with no draft redirects to 4a; the Dispute control on a disputable row is a TanStack Router `Link` (the 2.4 full-page-load shortcut is removed)

2. **Reason and expected amount** (R13, R14; FR-13, FR-14)
   **Given** screen 4a
   **When** the customer picks a reason and fills the expected amount
   **Then** the six reasons show as a Radix `RadioGroup` with the copy deck's label and explanation; Expected amount shows only for Wrong amount, directly below the group, and choosing another reason hides it and clears its value
   **And** the expected amount, trimmed, matches `^[0-9]{1,17}(\.[0-9]{1,2})?$`, is at least 0.00 and below the transaction amount, compared as integer cents parsed from the decimal strings (never `parseFloat` or `Number` on money; AD-4); it validates on blur and on Continue, and while valid "You're disputing R{difference}" shows live under it; for any other reason the disputed amount is the transaction amount; tests cover 0.00 accepted, one cent below the transaction amount accepted, the transaction amount refused, three decimals refused and a JSON-safe string sent (`"80.00"`, never a number)

3. **Note with a character count** (R13; FR-13)
   **Given** the optional note on 4a
   **When** the customer types or pastes
   **Then** a live count "{n} / 1,000" counts UTF-16 code units (`String.length`) after trimming; it is announced politely only at 900 and at 1,000; input past 1,000 is kept, never cut (no `maxLength`), and becomes the field error on Continue (EXPERIENCE.md, Note field; GOV.UK Design System, Character count)

4. **Accessible field and form errors** (R40, R13; NFR-CODE-6)
   **Given** invalid input on Continue, or a 400 `validation` response to Submit
   **When** the errors show
   **Then** the form is shadcn/ui `Field` with React Hook Form's `Controller` and a zod schema; each field sets `aria-invalid="true"` and an `aria-describedby` naming its error element itself, and the test asserts both on every field; an error summary at the top, titled "Check the details below", links to each field and takes focus, with `role="alert"` on a wrapper inside the focused summary (EXPERIENCE.md, Field errors and Accessibility Floor; GOV.UK Design System, Error summary; WCAG 2.2 SC 3.3.1)
   **And** a 400 on 4b returns to 4a with the draft's values and sets each `errors` key `reason`, `note` or `expectedAmount` on its field with that field's copy below; the API's one `expectedAmount` key covers four rules and the SPA never reads its message text (AD-3), so an `expectedAmount` key from the API shows "Enter an amount less than R{amount}, with at most 2 decimals." ({amount} the transaction amount), while the client-side checks keep their own lines; any other key, or a 400 with no `errors` (a body binding failure), shows the form-level error "We couldn't check your dispute. Review the details and try again." in the summary
   **And** the copy is: reason missing "Choose why you're disputing this transaction."; expected amount missing "Enter the amount you expected to pay."; not a valid amount "Enter an amount like 80.00, with at most 2 decimals."; not below the transaction amount "The expected amount must be less than R{amount}."; note too long "Your note must be 1,000 characters or fewer."

5. **One draft, one request, one key** (R15; FR-15)
   **Given** valid input on 4a
   **When** the customer chooses Continue and then Submit dispute, once or many times
   **Then** Continue builds one immutable request (`accountRef` and `transactionId` from the cached row, `reason`, the trimmed `note` or null, `expectedAmount` or null) and a draft key `"<crypto.randomUUID()>"`, the quoted RFC 9651 Structured Field String (AD-16); 4b reads it back (transaction, date, card or account, reason, note, "You're disputing R{amount}") with Change returning to 4a with the values kept
   **And** every attempt for the draft sends that same body and key through the generated mutation and `customFetch`, so each carries `X-XSRF-TOKEN` (ASVS 5.0 V3.5.1); continuing again with a request equal to the draft's keeps its key, and a different request gets a new key; while an attempt is pending Submit shows "Submitting…", has `aria-disabled="true"`, keeps focus and ignores presses (no `disabled`, so focus is not lost), and Change and Cancel are `aria-disabled="true"` and ignore presses too, until the attempt and its in-progress resends end; a 201 that arrives after the customer left 4b by browser Back still invalidates the queries and marks the draft submitted (criterion 7) but does not navigate
   **And** the draft and key live only in React state held above 4a and 4b: never in the URL, router state or `history.state`, storage, a query key, a span attribute or `console`; a reload loses them (EXPERIENCE.md, Session ended)

6. **Retries keep the request and key** (R15; FR-15)
   **Given** an attempt that does not end in a final answer
   **When** the response is 409 `idempotency-in-progress`, a network error, 503 `core-banking-unavailable`, 504 or another 5xx
   **Then** 409 `idempotency-in-progress` keeps "Submitting…" and resends after 1 second, at most 5 times, through the mutation's `retry` and `retryDelay`; after the fifth, and after a network error or 504, the notice is "We couldn't confirm your dispute was received. Try again. You won't create a second dispute." with Try again; 503 shows "We can't check this transaction with the bank right now. Try again." with Try again; another 5xx shows "Something went wrong on our side. Try again." with Try again and the reference
   **And** every Try again resends the draft's exact body and key; a test asserts each attempt, the five in-progress resends included, carries the same `Idempotency-Key` and an `X-XSRF-TOKEN`

7. **Submitted** (R13; FR-13)
   **Given** a 201 `DisputeResponse`, first time or as a replay
   **When** it arrives
   **Then** the SPA invalidates the transactions list query and every cached query whose key starts with `/api/v1/disputes` (`invalidateQueries`), announces "Dispute submitted" in the one polite live region, and navigates with `replace` to `/disputes/<disputeId>`, which renders the not-found screen until story 3.7 adds the route
   **And** Back from there never shows 4a or 4b for that draft again: a submitted draft sends the raise routes to `/transactions` (EXPERIENCE.md, Back and forward)

8. **Refusals and a lost session** (R13, R12; FR-13)
   **Given** a refused submit
   **When** the response is 409 `already-disputed`, 422 `not-disputable`, 422 `idempotency-mismatch`, 404 `not-found` or 401
   **Then** 409 `already-disputed` shows "This transaction already has a dispute. You can only dispute a transaction once." with "View dispute" to `/disputes/<disputeId>` from the body and "Back to transactions"; 422 `not-disputable` shows "This transaction can't be disputed any more." plus, for `reason` `outsideWindow`, "It's now older than 90 days. Contact the bank about this transaction." and, for `notEligible`, "Contact the bank about this transaction.", with "Back to transactions"; 404 shows "This transaction is no longer available." with "Back to transactions"; 422 `idempotency-mismatch` shows the unexpected-error notice with the reference and "Back to transactions", without Try again; 409 and 422 `not-disputable` invalidate the transactions list query so the row refreshes
   **And** each notice has `role="alert"` and shows only this copy and the `traceId` reference, never the body's `title`, `detail` or raw text (AD-3)
   **And** a 401 on 4a or 4b goes to the session-ended screen whose Sign in again carries `returnUrl=/transactions`, so no transaction ID from the route reaches the BFF login URL (AD-31)

## Boundaries

- Must not change: the generated client under `frontend/src/api/generated/` (story 3.3 regenerates it; nobody edits it), `customFetch`'s credentials and antiforgery behaviour, the 401 and 403 handling for every other route, and the transactions screen's loading, empty, error and Load more states (story 2.4).
- Must not change: `noToken.test.tsx` and its source scan, which already covers every new folder under `frontend/src/` through `appSourceFiles`.
- Not here: the dispute status badges on rows (story 3.10), the OpenTelemetry web SDK and the raise spans (story 3.11), the dispute detail and its "Dispute submitted" banner (story 3.7).
- CSP: the gateway sends `script-src 'self'; style-src 'self'` (story 2.5), so the screens add no inline `<script>`, no `<style>` element and no `style` attribute in markup; zod is configured with `z.config({ jitless: true })` if the pinned zod version compiles schemas with `new Function` (the builder checks that version's source or docs and records which).

### Request inputs and their bounds

| Input | Source | Bounds |
|---|---|---|
| `transactionId` | the route, matched to a cached row | the row's value, sent as is (API: 1 to 32 characters) |
| `accountRef` | the cached row | the row's value (API: 1 to 32 characters) |
| `reason` | `RadioGroup` | one of the six `Reason` values; required |
| `note` | textarea | optional; trimmed; at most 1,000 UTF-16 code units; empty after trimming is sent as null |
| `expectedAmount` | text input, `inputmode="decimal"`, "R" prefix | only for `wrongAmount`, else null; trimmed; `^[0-9]{1,17}(\.[0-9]{1,2})?$`; at least 0.00; below the transaction amount in integer cents |
| `Idempotency-Key` | `crypto.randomUUID()` | one per draft; sent quoted, `"<uuid>"` (36 characters inside the quotes; API: 1 to 255 printable) |

## Supervisor checks

Deliberate-break proofs, run by the supervisor in a throwaway copy, not the verifier.

- Make Try again mint a new key: criterion 6's same-key test fails.
- Remove `aria-describedby` from the expected amount field: criterion 4's test fails.
- Clear the key between attempts in the in-progress retry: criterion 6's five-resend test fails.

## Build facts

- Owner: frontend (frontend-builder).
- Flags: none.
- Lane: B.
- Mechanisms and sources: shadcn/ui *Forms* (`Field`, React Hook Form `Controller`, zod) per AD-31 (2026-10-30); Radix `RadioGroup`; TanStack Query mutation `retry` and `retryDelay`, `invalidateQueries`; TanStack Router `Link`, `<Navigate replace>` inside the session gate (built so, accepted at review; not `redirect` in `beforeLoad`), `navigate({ replace: true })`; GOV.UK Design System *Error summary*; RFC 9651 Structured Field String and draft-ietf-httpapi-idempotency-key-header; GOV.UK Design System *Character count*; WCAG 2.2 SC 3.3.1, 4.1.3.
- Packages: React Hook Form, zod and `@hookform/resolvers`, and the shadcn/ui `field`, `radio-group`, `input`, `textarea` and `label` components, each verified (publisher, downloads, repository, latest stable) and pinned exactly (AGENTS.md, Dependency verification); `npm audit` shows no new high finding.
- Touches:
  - `frontend/src/features/disputes/raise/` (new: the 4a and 4b screens, the draft state, the zod schema, the amount-in-cents helper, their tests)
  - `frontend/src/features/transactions/TransactionRow.tsx` (the Dispute control becomes a router `Link`)
  - `frontend/src/app/router.tsx` (the two raise routes)
  - `frontend/src/api/queryClient.ts` (the 401 `returnUrl` for the raise routes)
  - `frontend/src/api/problems.ts` (the new slugs: `already-disputed`, `not-disputable`, `idempotency-in-progress`, `idempotency-mismatch`, `not-found`, `validation`)
  - `frontend/src/components/ui/` (new shadcn/ui components)
  - `frontend/src/features/transactions/format.ts`, `frontend/src/features/transactions/notDisputableHint.ts` (shared with the raise screens; added after the build)
  - `frontend/src/test/setup.ts`, `frontend/src/test/support.tsx` (test setup and helpers; added after the build)
  - `frontend/package.json`, `frontend/package-lock.json`
- Consumed, not edited: `frontend/src/api/generated/` (story 3.3), `frontend/src/api/mutator.ts`, `frontend/src/app/LiveRegion.tsx`, `frontend/src/components/ProblemNotice.tsx`.

## References

- parent — docs/tickets/epic-3-disputes/epic-3-disputes.md, section Requirements
- architecture — docs/architecture/architecture.md, AD-3, AD-4, AD-16, AD-31
- design — docs/ux/mockups/dispute-new.html
- design — docs/ux/mockups/dispute-new-wrong-amount.html
- design — docs/ux/mockups/dispute-review.html
- design — docs/ux/mockups/dispute-review-retry.html
- design — docs/ux/mockups/dispute-already-disputed.html
- design — docs/ux/mockups/dispute-not-disputable.html
- experience — docs/ux/EXPERIENCE.md, Copy deck, Component Patterns, State Patterns, Problem-type mapping, Interaction Primitives
- standard — <https://ui.shadcn.com/docs/forms/react-hook-form>
- standard — <https://www.rfc-editor.org/rfc/rfc9651>
- standard — <https://datatracker.ietf.org/doc/draft-ietf-httpapi-idempotency-key-header/>
- standard — <https://design-system.service.gov.uk/components/character-count/>
- standard — <https://design-system.service.gov.uk/components/error-summary/>
- verification — docs/verification/3.6-challenge.md, docs/verification/3.6-prebuild-review.md, docs/verification/3.6-prebuild-security.md, docs/verification/3.6-review.md (Resolution)

## Notes

- Risk medium: a wrong key or a changed body on retry gives a second dispute or a false error; beyond the criteria, the supervisor reproduces the same-key break above.
- Decision: 2026-10-27 — the `Idempotency-Key` value is an RFC 9651 Structured Field String, so the SPA sends it quoted (AD-16).
- Decision: 2026-10-27 — links to screens not built yet render the not-found screen until their stories land.
- Decision: 2026-10-30 (human, D2) — forms use shadcn/ui's current `Field` with React Hook Form's `Controller` and zod; each field wires `aria-invalid` and `aria-describedby` explicitly, and criterion 4 tests it (AD-31, updated). Supersedes the 2026-10-27 `FormField` wording.
- Decision: 2026-10-30 (human, F7) — after a 401 from a raise screen, sign-in returns to `/transactions`; no identifier from the route goes in `returnUrl` (AD-31; criterion 8).
- Decision: 2026-10-30 (Sofia, pre-build review) — 3.6 split to stay within about 8 criteria (AGENTS.md, Definition of ready): 3.6 the raise and review screens (buildable once 3.3 merges), 3.10 the dispute badges on rows, 3.11 the browser spans. The title changed with it, the number kept.
- Decision: 2026-10-30 (Sofia) — 3.6 waits on 3.3 for the regenerated client (the header parameter and `DisputeResponse`), not on 3.9: the 201 invalidates by key prefix, so the history list story needs nothing from 3.6.
- Assumption: 2026-10-30 (Sofia) — the copy not in EXPERIENCE.md (the field error lines, the form-level error, the 503, 404 and `notEligible` lines in criteria 4, 6 and 8) is proposed here in the copy deck's voice; confirm at review.
- Assumption: 2026-10-30 (Sofia, challenger) — a Continue with a request equal to the draft's keeps the key, so a customer who opens Change and continues unchanged after an uncertain submit still replays rather than meeting `already-disputed`.
- Decision: 2026-10-30 (Sofia, 3.6 review) — an API 400 on `expectedAmount` shows one line naming the rules a customer can break, "Enter an amount less than R{amount}, with at most 2 decimals.", because the key alone cannot say which of the four rules failed and the SPA routes on keys, never on message text (AD-3). Proposed copy in the deck's voice; confirm with the other proposed copy.
- Decision: 2026-10-30 (Sofia, 3.6 review) — while an attempt is pending, Change and Cancel are inert like Submit (EXPERIENCE.md is silent; Idempotent submit only covers Submit): the attempt ends within about 5 seconds plus the request, and an inert control is plainer than a navigation that must be cancelled while a dispute may already exist. Browser Back cannot be held, so a late 201 after Back does not navigate but still marks the draft submitted.
- Decision: 2026-10-30 (Sofia, 3.6 review) — the error summary keeps focus and carries `role="alert"` on an inner wrapper, as GOV.UK Design System's Error summary does, which meets both EXPERIENCE.md lines (an error summary that takes focus; errors use `role="alert"`).
- Decision: 2026-10-30 (reviewer, accepted) — the row guard is a `<Navigate replace>` inside the session gate, not `redirect` in `beforeLoad`; criterion 1 and Build facts updated.
- Carried from 3.1 QA (2026-10-28): body binding failures (unknown `reason`, a JSON-number `expectedAmount`, malformed JSON) return 400 `validation` with no `errors` naming the field; criterion 4 maps them to the form-level error.
- Carried from 3.2 (2026-10-28): a replay returns the stored status, body and `Location` byte for byte; a 5xx (504 included) releases the key, so a retry with the same key is safe (AD-16).
