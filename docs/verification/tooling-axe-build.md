Axe checks now run in the component tests, but they do not catch the 3.6 escape. axe-core has no rule that requires a name on `role="radiogroup"`. Criterion 4 therefore failed as written.

Everything else passes: Vitest (161 tests, 8 of them new axe tests), `tsc --noEmit`, eslint, `prettier --check` and `npm run build`. Nothing is committed.

## What changed
- **Packages:** `vitest-axe@0.1.0` and `axe-core@4.14.0`, both pinned as devDependencies.
- **Package checks:**
  - `vitest-axe` has about 2.4M downloads a week and a single maintainer, and its repository is `chaance/vitest-axe`. It is the only maintained Vitest 5-compatible option: `@chialab/vitest-axe` has about 7k downloads a week and peers `vitest ^3||^4`.
  - Its last release was January 2025, which is old, but it ran under Vitest 5.0.5 with no problems apart from the types below.
  - `axe-core` is published by Deque and has about 108M downloads a week. It is `vitest-axe`'s runtime dependency, so I pinned it directly too.
- **Matcher:** `src/test/setup.ts` registers `vitest-axe/matchers` with `expect.extend`.
- **Types:** the new `src/test/axe.d.ts` declares `toHaveNoViolations` in the Vitest module. The package's own `vitest-axe/extend-expect` types extend the legacy `Vi` namespace, which Vitest 5 no longer reads, and `tsc` rejected them. I added no lint suppressions.
- **Screens covered:** every screen under `src/features` and `src/pages`, plus the inactivity dialog (`src/app/InactivityWarning.tsx`). I found no violations on main, so no component changed and no rule is loosened.

## The break
- **Unnamed radiogroup:** a Radix `RadioGroup` with no accessible name passes axe, even with every rule tag enabled, experimental and best-practice included. That is the 3.6 escape, and this check does not catch it.
- **What it does catch:** an unnamed `alertdialog`, `progressbar` or `listbox` fails (`aria-dialog-name`, `aria-progressbar-name`, `aria-input-field-name`), so the integration works for those roles.
- **Cleanup:** the throwaway test is deleted and not in the diff.

## Decision for you
Your call: how to cover radiogroup names. The usual fix is a Testing Library assertion such as `getByRole('radiogroup', { name })` on any screen with a radiogroup. A small custom axe rule is the other option, but it is a custom mechanism, so I did not add either.

## Other notes
- jsdom prints "Not implemented: HTMLCanvasElement.getContext" while axe runs. It is harmless noise, and I did not silence it.
- I ran no smoke check, because there is no new UI or API surface.

```json
{
  "status": "built",
  "worktree": "/Users/njabulomnisi/Projects/Dojo/disputedesk/.claude/worktrees/axe",
  "criteria": [
    {"id": "1", "test": "vitest-axe@0.1.0 and axe-core@4.14.0 pinned in frontend/package.json devDependencies"},
    {"id": "2", "test": "frontend/src/test/setup.ts registers the matcher; frontend/src/test/axe.d.ts types it (tsc passes)"},
    {"id": "3", "test": "routing.test.tsx: start page, session-ended screen and not-found screen have no violations; TransactionsScreen.test.tsx: rows, loading, empty and 503 error states have no violations; InactivityWarning.test.tsx: open warning dialog has no violations"},
    {"id": "4", "test": "NOT MET for radiogroup: a throwaway unnamed Radix RadioGroup passed axe with all rule tags. The same throwaway run failed on unnamed alertdialog, progressbar and listbox. The throwaway file is deleted."}
  ],
  "standards": [
    {"mechanism": "Custom matcher registered with expect.extend", "source": "https://vitest.dev/guide/extending-matchers"},
    {"mechanism": "axe-core accessibility rules via vitest-axe (a fork of jest-axe)", "source": "https://github.com/chaance/vitest-axe"},
    {"mechanism": "Rule set run: axe-core defaults, none disabled", "source": "https://dequeuniversity.com/rules/axe/4.14"}
  ],
  "breaks": [],
  "assumptions": [
    "vitest-axe 0.1.0 is old (January 2025) but works on Vitest 5.0.5; only its bundled types needed replacing",
    "color-contrast is not checked, because jsdom has no layout engine"
  ],
  "open_questions": ["How to cover the radiogroup-name escape: a getByRole('radiogroup', { name }) assertion on each radiogroup screen, or a custom axe rule?"],
  "conflicts": ["Task criterion 4 (axe must fail on an unnamed RadioGroup) is false for axe-core 4.14.0, which has no rule that requires a name on role=radiogroup"]
}
```
