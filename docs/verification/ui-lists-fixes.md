# ui-lists: fix round 1

Base: `2f897c37b3b58b11119c6126123213f98defeb43` with ui-tokens-icons (fix round 3) and ui-notices-states (fix round 1), uncommitted. The story's tree so far, with QA's e2e edits and the new `spa-lists-layout.spec.ts`, is `ui-lists-r1.patch`.

| # | Stage | Severity | File | Finding | Fix |
|---|---|---|---|---|---|
| 1 | QA | medium | `frontend/src/features/transactions/TransactionRow.tsx`, `features/disputes/history/DisputeRow.tsx` | At 200% root font and 320 to 360px a nowrap status badge with `justify-self-end` overflows to the left of the `overflow-hidden` panel and is clipped ("Under review (prioritised)" starts 151px outside the panel at 360px; prioritised and Resolved badges cut off on /disputes). | Let the badge wrap or shrink below 768px so it stays inside the panel; theme tokens only. Add a unit class test and a break; check in a real browser at 320 and 360px with the root font at 100%, 150% and 200%. QA adds an e2e case at 360px and 200% to `spa-lists-layout.spec.ts`. |
| 2 | review and QA | low, fixed here (same file, one class) | `TransactionRow.tsx:97` | The phone Dispute link's focus ring is clipped at the bottom on the panel's last row (ring bottom 563.56 against panel bottom 559.56). | `focus-visible:-outline-offset-3` on the Dispute link. |
| 3 | review and verify | low, fixed here (same file) | `TransactionsScreen.tsx:56` | The skeleton's band placeholders are `div`s directly inside the `ul`. | `<li aria-hidden="true">` placeholders; the list still exposes 5 items. |

Q2 (merchant breaking per character at 320px/200%) and the known header scroll at 200% go to `lows.md`.
