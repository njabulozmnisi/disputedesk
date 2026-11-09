# ui-raise-review: fix round 1

Base: `2f897c37b3b58b11119c6126123213f98defeb43` with ui-tokens-icons (fix round 3) and ui-notices-states (fix round 1) applied, uncommitted. The story's whole tree so far is `ui-raise-review-r1.patch`.

| # | Stage | Severity | File | Finding | Fix |
|---|---|---|---|---|---|
| 1 | review | medium | `frontend/src/features/disputes/raise/ReviewScreen.tsx:70` | `NumberRow` shows the masked card or account (`492345•• •••• 3456`, `•••• 4821`) with no screen-reader form; review used `MaskedNumber` before, which hides the printed form and reads "card ending 3456" or "account ending 4821" (EXPERIENCE.md Accessibility). A regression with no test. | Wrap the printed form in `aria-hidden="true"` and add an sr-only "card ending {lastFour}" / "account ending {accountLast4}", as `MaskedNumber` does; assert the spoken form and the hidden printed form in `ReviewScreen.test.tsx`, with a break. |

Lows (review low 2 to 4) go to `docs/verification/lows.md` at merge. The `lib/utils.ts` fix stays in this story (review, question 1).

## Fix round 2

| # | Stage | Severity | File | Finding | Fix |
|---|---|---|---|---|---|
| 2 | QA | medium | `frontend/src/features/disputes/raise/ReviewScreen.tsx` (the review `dl` grid) | The 9rem label column scales with the root font size: with text-only scaling the value column collapses (360px at 150%: 22px; at 200%: 0px and a 452px scroll width; 320px at 150%: 0px), failing WCAG 2.2 SC 1.4.4 and 1.4.10. Criterion 5's "9rem at every width" conflicts with the standard; the standard wins (accessibility is never cut). | Bound the label column so the value column always keeps room, for example `min(9rem,40%)` in theme-token terms, no raw px. Keep 9rem where it fits. Add a class test and a break; check in a real browser at 320 and 360px with the root font size at 150% and 200% (no horizontal scroll, value column readable) and at 1280px (still 9rem). QA adds this text-scaling check to `spa-raise-review-axe.spec.ts` or a sibling. |

QA's e2e edits (dispute-raise.spec.ts Change lookups, the new spa-raise-review-axe.spec.ts, and the copied base edits) are in the QA tree and stay as they are.
