# ui-tokens-icons: postmortem for a second fix round

Trigger (AGENTS.md, Continuous improvement): a second fix round, opened 2026-11-08.

## What happened

- **Round 1 (review, medium):** a unit test meant to prove that the top navigation has no icon could never fail. `queryByRole('img')` does not match an `<svg>` that has no role. The reviewer caught it; the builder's breaks did not, because no break targeted that boundary.
- **Round 2 (QA, medium):** the spec raised the header home link to 44px, as the pre-build review asked. At 320px the focused "Skip to main content" link now covers all but 6px of it, and axe fails WCAG 2.2 SC 2.5.8 ("partially obscured") in `linking-screens` and `linking-queue`. Before the change, the skip link hid the home link completely, which axe does not flag.

## Why the earlier stages missed round 2

- The pre-build review asked for the 44px home link but did not check what overlaps it in a focus state.
- jsdom has no layout, so the builder's unit tests and the verifier's breaks cannot see geometry. Only a real browser at 320px with keyboard focus shows it, and that ran only in QA.
- The `ui-notices-states` builder ran a `vite preview` smoke with stubbed API routes at 360px and 1280px, but no axe pass and no keyboard walk. The `ui-tokens-icons` builder ran no browser at all.

## Proposed countermeasures (for the human; a skill change asks)

1. **Deterministic check:** none fits cheaply. The axe target-size rule already runs in e2e, which is the authoritative place, and it caught the problem.
2. **One `build-story` "Known pitfalls" line, for frontend builders:** "A change to the size or position of an interactive element: run a throwaway headless Chromium pass against `vite preview` (API stubbed with `page.route`) at 320px, with axe and a keyboard walk that focuses the skip link, before reporting."
3. **One `review-story` pre-build line:** "A criterion that changes a tap target's size names the elements that can overlap it in a focus or hover state."

Escapes category: environment or tooling (no layout in unit tests), plus a spec gap (overlap in a focus state).
