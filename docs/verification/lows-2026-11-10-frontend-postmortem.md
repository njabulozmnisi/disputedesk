# Postmortem: lows-2026-11-10 lane 2's second fix round (2026-11-11)

Trigger: a second fix round (AGENTS.md, Continuous improvement).

| Round | Cause | Stage that caught it | Stage that should have | Category |
|---|---|---|---|---|
| 1 | A refused paste then a six-digit edit kept a stale code error (input-otp fires `onComplete` only from a shorter value); the wrapped phone tab bar hid the end of the page at 150% and 200% text | verifier (error), reviewer (tab bar) | the builder | missing test, environment (layout) |
| 2 | The two-row tab bar hides focused links (SC 2.4.11), and a `sticky` bar likely trips axe's target-size rule in the e2e checks | reviewer (fix round 1) | the builder of round 0 and the first review | environment (layout) |

## Pattern

1. A low about one symptom (carried row 17: the header scrolls sideways at 200% text) grew into a layout redesign of
   a second component (the phone tab bar). Each round fixed the last finding of that redesign and found the next.
2. jsdom has no layout, so the class-name tests could not see any of it; only the headless Chromium measurements and
   the reviewer's own measurements did. The build measured overflow and clipping but not focus visibility, which is
   what failed in round 2.
3. The reviewer missed SC 2.4.11 in the first review and recorded it as its own escape.

## What was done

The supervisor stopped the chain: round 2 returns the tab bar to its one-row form from `main`, and the tab-bar part of
row 17 goes back to `lows.md` with the reviewer's full fix written up, to be done as its own change.

## Proposed countermeasures (for the retrospective, need a human decision)

- A lows batch fixes the row as reported. A fix that changes a second component's layout is split out as its own
  change, not added to the batch (one line in the supervisor's dispatch notes for lows batches).
- For any layout change, the headless-browser check measures focus visibility (a Tab walk with every focused element
  fully in view, WCAG 2.2 SC 2.4.11) as well as overflow and clipping (one line in the build-story skill's Known
  pitfalls).
