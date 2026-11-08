---
title: DisputeDesk UI refresh, handoff for implementation
status: ready
updated: 2026-11-08
companions:
  - DESIGN.md
  - EXPERIENCE.md
  - mockups/index.html
---

# DisputeDesk UI refresh: implementation handoff

## What this is

A visual and interaction pass over every customer screen. It changes presentation only: no API, routing, authorisation, idempotency or session behaviour changes. Brand colours are unchanged. One theme token pair is added.

Implement it in the existing SPA (React, shadcn/ui on Tailwind v4, TanStack Query). Where this file says "component", find the existing one that renders that UI and change it. Do not create a parallel component.

## Sources of truth, in order

1. [DESIGN.md](DESIGN.md) (how it looks) and [EXPERIENCE.md](EXPERIENCE.md) (how it behaves). Both were updated on 8 Nov 2026 for this pass and win on any conflict.
2. [mockups/](mockups/index.html): 33 static pages plus the index, one per screen and state. Open `mockups/index.html` in a browser. `mockups/app.css` is the reference CSS for every component below, written only in theme tokens.
3. `mockups/screenshots/`: captures at 360px and 1280px, plus a dark capture of the start page.
4. This file: the change list and acceptance checks.

Mock notes (dashed boxes) and the focus rings drawn on error summaries and on "Keep dispute" are annotations, not UI to build.

## 1. Theme file

`frontend/src/theme/tokens.css`:

- Add `--amount-col: 9rem;` and `--action-col: 14rem;`. They were in DESIGN.md but missing from the theme file. Map them in `@theme inline` if the list grid uses Tailwind utilities.
- Do not change or add any colour value.

## 2. Icons

Replace every text-glyph icon (➚ ◔ ▲ ✓ ✕ ↩ ⓘ ⚠ ☰ ⚑) with `lucide-react` icons. These glyphs fall outside the Nunito Sans latin subset, so they rendered differently on each device. Every icon is 16px with a 2px stroke, uses `currentColor` and has `aria-hidden="true"`; the text beside it carries the meaning.

| Use | Lucide icon |
|---|---|
| Submitted badge | `Send` |
| Under review badge | `Search` |
| Under review (prioritised) badge | custom `SearchPriority`: Lucide `Search` plus an up arrow inside the lens (path in `mockups/dispute-prioritised.html`) |
| Resolved · Upheld badge | `Check` |
| Resolved · Rejected badge | `X` |
| Resolved (transaction row) | `Flag` |
| Withdrawn badge | `Undo2` |
| Not-disputable hint, info notices | `Info` |
| Danger notices, field errors | `TriangleAlert` |
| Success notices | `CircleCheck` |
| Rate limited notice, session ended | `Clock` |
| Locked screen, start page assurance line | `Lock` |
| Dispute link, disputes row | `ChevronRight` |
| Tab bar: Transactions / Disputes | `List` / `Flag` |
| Not found | `Search` |

## 3. Change list

Each item: what changes, then how to check it.

### 3.1 Wordmark
The cobalt dot moves after the name, like a full stop: 8px, `primary`, baseline-aligned, 2px gap. Same in the header on every screen and on the hosted pages.
- Regenerate `wordmark.png` and `wordmark-dark.png` to match (story 2.6 names them), then re-run the label-policy provisioning.

### 3.2 Transactions list (`/transactions`)
- Lede: "Your card and account transactions, newest first. You can dispute those from the last 90 days."
- One panel. From 768px it opens with a column header row (Transaction, Amount, Dispute), which is `aria-hidden` because each row already reads in full.
- Day headings become bands inside the panel: `background` fill, 14/700 `muted-foreground`, hairlines above and below.
- From 768px every row is a three-column grid: `minmax(0,1fr) var(--amount-col) var(--action-col)`, with 24px column gap. The amount is right-aligned and tabular. The action column is left-aligned.
- Phone: unchanged structure. Amount top right, the Dispute link under it, and any badge on its own right-aligned line.
- **Dispute link:** text "Dispute" plus `ChevronRight`, at least 44px tall, no underline until hover. The accessible name includes the merchant ("Dispute ZXP*GLOBALSVCS 0800").
- **Status badge on a row:** a resolved dispute shows plain **Resolved** with `Flag` in the neutral tone. Never the outcome (decided 30 Oct, story 3.10). The old mockup was wrong here.
- **Not-disputable hint:** `Info` icon (14px) plus the hint text in `status-neutral-fg`.
- Check: at 1280px every amount, Dispute link and badge lines up in its column. At 360px nothing scrolls sideways and no merchant name is squeezed by a badge.

### 3.3 Disputes list (`/disputes`)
- Each row is a single `<a>` to the detail containing merchant (cobalt, 700), "Reason, raised date" caption, amount, badge and a `ChevronRight`. Hover tints the row with `background` and underlines the merchant.
- From 768px the row uses the same columns as transactions, plus a 20px chevron column. On phones the badge drops to its own line and the chevron is hidden.
- Check: one tab stop per row; the link's name reads merchant, reason, date, amount and status.

### 3.4 Step indicator (new component `StepProgress`)
- A text label plus a two-segment bar (4px, pill, `primary` for done steps and `border` for the rest). The bar is `aria-hidden`; the label carries the meaning.
- Used on `/link` ("Link your login, step 1 of 2"), `/link/otp` ("Link your login, step 2 of 2"), the raise form ("Step 1 of 2: reason and note") and review ("Step 2 of 2: review and submit"). Not on the 409 and 422 result screens.

### 3.5 Raise form (`/transactions/:id/dispute`)
- The transaction being disputed sits at the top of the form as a quiet block (`background` fill, `rounded.md`), not a row with a hairline.
- Wrong amount: under the Expected amount field, the live difference "You're disputing R349.00" in 14/700 `heading` (behaviour already in EXPERIENCE.md, now drawn).
- The buttons sit below a hairline.

### 3.6 Review (`/transactions/:id/dispute/review`)
- The read-back is a `dl`: a 9rem label column and a value column, hairlines between rows. "Change" sits top right beside the hero amount and is at least 44px tall, with the accessible name "Change your dispute".
- "You can dispute a transaction only once…" becomes a neutral notice with `Info`.

### 3.7 Notices (shadcn `Alert`)
- Every notice has a leading tone icon: danger `TriangleAlert`, info `Info`, success `CircleCheck`, neutral `Info`, rate limited `Clock`.
- **Problem notice:** the sentence, the next step, then "If this keeps happening, contact the bank and quote this reference." Below that, on its own row after a 20% `currentColor` hairline: "Reference", the `traceId` in mono, and the Copy pill. The Copy pill is at least 44px tall (it was about 28px).
- **Error summary:** a danger notice titled "Check the details below" with a list of links to fields. Focus moves to it, as before.
- **Field error:** `TriangleAlert` (14px) plus the message, under the field.

### 3.8 Full-screen and empty states (new component `StateSurface`)
- One flat surface (`card`, hairline, `rounded.lg`) with a 48px `muted` icon tile (24px icon in `heading`), the heading, the sentences, then one action.
- Used on: session ended (`Clock`), locked (`Lock`), dispute not found (`Search`), transactions empty (`List`, no button), disputes empty (`Flag`, "Go to transactions").

### 3.9 Linking help block
- Every linking screen ends with the same block after a hairline: "Need help?" (16/700), then on the OTP screens "Didn't get a code? Start again.", then "Can't link online? Contact the bank." This is the consistent-help criterion, WCAG 2.2 SC 3.2.6.
- OTP help text: "The code expires 5 minutes after we send it. You can paste it, or let your phone fill it in."
- The one-time code is six cells, built with shadcn/ui's [Input OTP](https://ui.shadcn.com/docs/components/input-otp) in the project's `new-york` style and styled to this section (decided 8 Nov 2026). Autofill (`autocomplete="one-time-code"`), the numeric keypad, paste, validation and submit behave as before.
- The active OTP cell gets a 4px `primary-subtle` halo outside its 2px `ring` border.
- Rate limited: the inputs are disabled (`muted` fill, `border` edge, `muted-foreground` text) and so is the Continue button (`muted` fill). It is not shown at reduced opacity.

### 3.10 Dispute detail (`/disputes/:id`)
- The summary card stays the only elevated object. The timeline now sits on a flat surface (`card`, hairline, `rounded.lg`) with "Timeline" as its h2. For open disputes, "We'll update the timeline as your dispute moves." sits inside it, below a hairline.
- New summary row **Disputed on** (for example "26 Oct 2026"). Take it from the detail response's `createdAt`, the moment the dispute was submitted (decided 8 Nov 2026: the contract guarantees it, while the timeline's order is a mapping convention); no new API field is needed.
- Withdraw (open disputes only) moves to the card's foot, after a hairline, with this sentence above it: "Changed your mind? You can withdraw while the dispute is open. You can't dispute this transaction again after you withdraw."
- Every timeline entry keeps its status as the customer sees it (PRD FR-17). On the upheld and rejected lines the copy-deck sentence sits beside that status: "The bank agreed with you." and "The bank didn't uphold your dispute." (decided 8 Nov 2026).
- Rejected: the guidance becomes its own flat surface below the timeline. Heading "If you disagree with this outcome", then two numbered steps: "You can complain to the bank first." and "If you're still not satisfied after the bank has answered your complaint, you can take it to the National Financial Ombud Scheme South Africa, an independent service that is free for customers."
- The just-submitted banner is a success notice with `CircleCheck`.
- Below 1024px everything stacks: card, then timeline, then guidance.

### 3.11 Withdraw dialog
Behaviour is unchanged: `AlertDialog`, initial focus on "Keep dispute", Esc keeps the dispute. Visually, the buttons sit below a hairline. On phones it is a bottom sheet.

### 3.12 Start page
Under the buttons, add the assurance line with `Lock` (13px caption): "You'll confirm it's you with your ID number, account number and a one-time code."

### 3.13 Loading skeletons
- Transactions: the column header, day bands, and rows with three skeleton bars in the real columns.
- Detail: a skeleton summary card plus a timeline surface with three entries (dot and two bars each).
- `aria-busy` stays as it is.

## 4. Out of scope

- Any API, route, auth, session, idempotency or retry behaviour.
- Colour values, type scale and radius scale.
- The hosted sign-in and sign-up pages beyond the regenerated logo PNGs. The provider keeps its own shapes and spacing; the mockups show those pages with DisputeDesk shapes, but they are not a pixel target.

## 5. Acceptance

- Each screen matches its mockup at 360px and 1280px, light and dark (`prefers-color-scheme`).
- No horizontal scroll at 320px or at 200% zoom.
- Every interactive element is at least 44px tall, including the Dispute link, "Change" and the Copy pill.
- No text glyph is used as an icon anywhere (search the source for the characters listed in section 2).
- axe passes in the Playwright suite on every route and state that has a story.
- Keyboard: each disputes row is one tab stop; the transactions column header is not announced; focus order is unchanged.
- Existing unit and e2e tests pass. Update snapshot and visual baselines deliberately, one reviewed commit per area.

## 6. Suggested order

1. Theme tokens, icons and wordmark (3.1, section 2). Every later item depends on these.
2. Notices, field errors and `StateSurface` (3.7, 3.8). These are shared by many screens.
3. Transactions and disputes lists (3.2, 3.3, 3.13).
4. Linking (3.4, 3.9).
5. Raise and review (3.5, 3.6).
6. Detail and withdraw (3.10, 3.11), then the start page (3.12).
