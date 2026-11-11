# lows-2026-11-10, lane 2 (frontend): fix round 1

The medium findings from the post-build stages, for one fresh builder. Records: `lows-2026-11-10-frontend-review.md`,
`lows-2026-11-10-frontend-verify.md`.

## M1 (verifier): a stale code error survives a full-length edit

`OtpScreen.tsx` clears the `code` error only in input-otp's `onComplete`, and input-otp calls `onComplete` only when
the previous value was shorter than six characters (`node_modules/input-otp/dist/index.mjs`). After a refused over-long
paste into a field that already holds six digits, editing or pasting so that it still holds six digits leaves "Enter
the 6-digit code." showing. The new test starts from an empty field, so it misses this.

Fix: clear the refused-paste error whenever the field holds a valid six-digit value, whatever the previous length
(for example from the field's `onChange`), keeping today's rules: no submit by itself, the wrong-code message and the
error summary behave as rows 12 and 13 now specify. Test the path the verifier names first: a field holding six
digits, a refused 7-digit paste, then an edit that keeps six digits.

## M2 (reviewer): the wrapped phone tab bar hides the end of the page

`Navigation.tsx:37`: the flex-wrap tab bar becomes two rows at 320px with 150% and 200% text (164px and 218px tall),
taller than the `pb-24` space `RootLayout.tsx:74` keeps for it (144px and 192px), so 44px and 58px of the page end (the
footer notice) stay under the fixed bar (WCAG 2.2 SC 1.4.4, 1.4.10). Before this lane the bar stayed one row and fit.

Fix: the simplest layout that keeps the whole page reachable at 320 and 360px with 100%, 150% and 200% text, for
example a `sticky bottom-4` bar in the layout's flex column without the `pb-24` reserve, or one row whose segments
shrink. The reviewer's L8 (`min-w-fit` lets "Transactions" run about 13px past the bar at 320px and 200%) should close
with it. Measure the result in headless Chromium as the build did.

## Not in this round

- The e2e focus check at `e2e/tests/linking-screens.spec.ts:220` (both stages): QA updates it in the same batch.
- The reviewer's lows L2 to L7 and the verifier's notes go to `lows.md`; L1 and L5 are docs the supervisor carries.

# Fix round 2 (the last)

Fix round 1 closed M1 and M2, and its review (`lows-2026-11-10-frontend-review.md`, "Fix round 1") found two more
medium problems, both caused by round 0 making the phone tab bar wrap onto two rows:

- R1-M1: at 150% and 200% text a focused link can sit entirely under the two-row bar (WCAG 2.2 SC 2.4.11); the
  one-row bar from before this lane hid none.
- R1-M2 (likely): with the bar `sticky`, axe-core's target-size rule compares it with the rows under it, so the
  `wcag22aa` e2e checks at phone widths may fail.

Supervisor's decision: rather than stack more layout on the wrapping bar, return the tab bar to its one-row,
`fixed` form from `main`.

Fix:
- `frontend/src/app/Navigation.tsx`: `TabBar`, `tabLinkClass` and the comment back to `main` (f6193f5), so the bar is
  `fixed inset-x-4 bottom-4`, one row, no `flex-wrap`, no `min-w-fit`, `px-3`.
- `frontend/src/app/RootLayout.tsx`: back to `main`, with the linked layout's `pb-24 md:pb-0` reserve.
- `routing.test.tsx`: remove the tests for the wrapping and sticky tab bar; keep the header-row wrap test.
- Breaks: drop the ones for the tab bar (wrap, sticky, reserve, `px-2`); keep every other break and rerun them.
- Keep everything else from rounds 0 and 1 unchanged, including M1's `onChange` fix and the header-row wrapping.
- Measure at 320 and 360px with 100%, 150% and 200% text that the page end is reachable and no focused link sits
  wholly under the bar, as before this lane.

What stays open, back in `lows.md`: carried row 17's tab-bar part. At 320px and 200% text the one-row bar pushes
"Disputes" partly off-screen. The reviewer's fix for a later change: a fixed bar with a two-row rem reserve
(`pb-32` below md) plus `scroll-padding-bottom` on `html` (WCAG technique C43).
