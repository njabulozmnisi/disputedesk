# Epic 9 decisions

Human decisions on the epic's `[decision]` points, batched once per epic (AGENTS.md, Story flow). The decisions before the stories were written are in `docs/decisions/ad-012-github-flow-ci.md` (its table).

| Story (point) | Question | Answer | Consequence |
|---|---|---|---|
| 9.3 (Sofia's assumption) | Add a code-scanning rule for CodeQL to `main`'s ruleset? | **Yes** (2026-11-05). | The ruleset carries `code_scanning` for CodeQL (high or higher security alerts, error alerts); if GitHub does not offer it on a user-owned public repository, the gap goes to `docs/production-path.md`. |
| 9.3 (pre-build review P21) | After 9.3, each story push and merge is a plain `git push` and `gh pr merge`, which the guard prompts on; accept the prompts, or allow the exact forms the 2026-10-30 standing approval covers? | **Allow the exact forms** (2026-11-05). | In the main session only and with no prompt, the guard allows exactly `git push origin feature/<id>` and `gh pr merge <n> --squash --auto --match-head-commit <40-hex>` (each as a full match of the whole command, with the owner's `GH_TOKEN` prefix where the review's P1 requires it); every other push, merge, force push, `gh api` write and `gh repo edit|delete|rename|archive` still asks the main session and is denied to a subagent. |
