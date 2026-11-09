# ui-tokens-icons: fix round 1

Base `2f897c37b3b58b11119c6126123213f98defeb43`. The story's change so far is the patch the brief names.

| # | Stage | Severity | File | Finding | Fix |
|---|---|---|---|---|---|
| 1 | review | medium | `frontend/src/app/routing.test.tsx:374-378` | The "top navigation with text only" check uses `queryByRole('img', { hidden: true })`. An `<svg>` has no implicit role in `@testing-library/dom` 10.4.2 and Lucide adds none, so the check can never fail and criterion 4's boundary "`TopNav` keeps text only" is unproved. | Assert `getByRole('navigation', { name: 'Main' }).querySelector('svg')` is null, and add a break that puts a Lucide icon in a `TopNav` link and makes the test fail. |

Lows from the review and the verifier go to `docs/verification/lows.md` at merge; they open no round.

## Fix round 2

| # | Stage | Severity | File | Finding | Fix |
|---|---|---|---|---|---|
| 2 | QA | medium | the skip link in the app shell (find it: `Skip to main content`) | At 320px the focused skip link (x8 y8, 181x40) overlaps the now 44px home link (y10 to y54), leaving 6px visible; axe fails WCAG 2.2 SC 2.5.8 target-size "partially obscured" in linking-screens and linking-queue. | Make the focused skip link either fully cover the home link or sit clear of it, in theme tokens only; keep the skip link first in tab order and visible on focus (WCAG 2.4.1). Add a unit test of the chosen geometry class and a break. QA re-runs the two linking projects. |

QA's e2e edits (spa-disputes-history.spec.ts:282-283, spa-transactions.spec.ts:331) are part of the patch and stay as they are.

## Fix round 3 (authorised by the human on 2026-11-08, after two rounds)

| # | Stage | Severity | File | Finding | Fix |
|---|---|---|---|---|---|
| 3 | QA re-run | medium | `frontend/src/app/RootLayout.tsx` (skip link) | Round 2 put the focused skip link in flow: focusing it shifts the page 60px, and a pointer click made while it is focused is lost when it blurs (WCAG 2.2 SC 2.5.2, 3.2.1); linking-queue fails at `e2e/tests/linking-screens.spec.ts:478`. | The human's decision: float the skip link again (absolute, out of flow), aligned with the header's home link and at least as tall and as wide, so it fully covers the home link while focused: no partial obscuring (SC 2.5.8, the state axe accepted before this story), no layout shift. Theme tokens only. Pin the alignment with a unit class test plus a break, and a real-browser check at 320 and 1280px (skip link box contains the home link box when focused; nothing else moves). QA re-runs linking-screens, linking-queue and journeys. |
