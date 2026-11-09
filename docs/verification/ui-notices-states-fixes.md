# ui-notices-states: fix round 1

Base: `2f897c37b3b58b11119c6126123213f98defeb43` plus the ui-tokens-icons change after its fix round 2 (`ui-tokens-icons-r2-fixed.patch`). This story's own change is `ui-notices-states-only.patch` (25 files, no package change).

| # | Stage | Severity | File | Finding | Fix |
|---|---|---|---|---|---|
| 1 | review | medium | `frontend/src/components/ProblemNotice.tsx:48` (with `ui/alert.tsx:71`) | The reference row is a grid item of `AlertDescription` (`justify-items-start`), so its `border-t` hairline stops at the Copy pill instead of crossing the notice; it shows at the 880px content width with a 55-character trace ID. | Stretch the row (`justify-self-stretch`) and prove it with a class assertion plus a break; a real-browser check at 1280px if possible. |
| 2 | verify (review ranked it low; the supervisor ranks it medium, because the story's Verify line forbids raw px in the diff) | medium | `frontend/src/components/ProblemNotice.tsx:53` | The rewritten Copy pill adds `border-[1.5px]`, a raw px value in a component. | Build the Copy pill from the existing `Button` (`components/ui/button.tsx`), which already carries DESIGN's 1.5px outline, with `min-h-(--tap-target)`; keep its behaviour and tests ("Copied" for 2s, keeps "Copy" on failure). |
| 3 | verify | low, fixed here because it is in the record this round rewrites | `docs/verification/ui-notices-states-build.json` | Each break keys its command as `test`; `scripts/run-breaks.sh` reads `test_method`. | Use `test_method`. |
