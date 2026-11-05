# AGENTS.md

Rules for any AI assistant or person working in this repo, whichever tool runs it. Area detail lives in
[docs/conventions/backend.md](docs/conventions/backend.md) and
[docs/conventions/frontend.md](docs/conventions/frontend.md). A tool's own instruction file (for example `CLAUDE.md`) adds only
that tool's mechanics and never overrides these rules.

## Priority order

Correctness, then security, then data integrity, then maintainability, then performance, then brevity.

## How to work

- Read before writing. Make the smallest change that satisfies the acceptance criteria. Leave the tree green.
- Framework first: check the .NET 10 docs (Microsoft Learn MCP, or `npx @microsoft/learn-cli`) before writing any abstraction. Use the built-in way where one exists: minimal API validation, ProblemDetails, `TimeProvider`, built-in OpenAPI, the built-in OIDC and cookie handlers, the built-in rate limiter.
- **Use established standards; do not invent mechanisms.** Implement every mechanism with the framework's documented approach (Microsoft Learn for .NET) or an established industry standard (RFCs, OWASP, OpenTelemetry conventions). A custom mechanism is acceptable only where no standard exists, and the report states why. Cite the source for each mechanism in the report. Where more than one standard approach fits, choose the one that is simplest to explain. If a design cannot be explained to an engineer new to the repo in one or two sentences, it is too complex and must be simplified.
- Every abstraction earns its place. No MediatR, AutoMapper, Swashbuckle or Duende; no single-implementation interfaces except `ICoreBankingClient`, `ISimSwapCheck`, `ISmsSender` and `IAppDbContext`.
- **Stupid simple and logical code.** Write the plainest code that meets the criteria, so a developer new to the repo understands it on first read: obvious control flow, early returns, short methods, descriptive names, no cleverness, no speculative options or extension points, no wrapper that only forwards calls. Prefer a few lines of plain duplication over a premature abstraction; abstract on the third real case. Before reporting, reread the diff and cut anything not needed.
- Over-engineering is the standing risk. Meet each quality area once and well; anything bigger goes in `docs/production-path.md`. Mark a deliberate shortcut in code with `// shortcut: <the limit>, <when to upgrade>`.
- Use the terms in [docs/glossary.md](docs/glossary.md). Don't invent synonyms.

## Stakes tiers

| Tier | Examples | Ceremony |
|---|---|---|
| Trivial | typo, comment | Change, run the affected check |
| Small | one endpoint field, one component state | Test first, full area checks |
| Feature | a story | The full story flow: builder, verifier, QA, reviewer |
| High-stakes | auth, identity and account linking, customer data, money, migrations | Feature flow plus the security agent and a human review of the diff |

## Definition of ready

A story is ready to build only when:

- every criterion is testable and tagged to a requirement;
- each criterion names the mechanism that implements it and the documented source for it (Microsoft Learn, the RFC, OWASP);
- every request input is listed with its bounds;
- it has no open questions;
- it has at most about 8 acceptance criteria; a larger story is split into vertical slices;
- a Feature or High-stakes story has had its pre-build review (`docs/verification/<story>-prebuild-review.md`): its `[story]` points are fixed in the story and a human has decided on its `[decision]` points (`docs/verification/epic-<epic>-decisions.md`).

## Ticket numbering

Epics and stories are numbered in build order; [docs/tickets/README.md](docs/tickets/README.md) lists them all.

- Epic folder: `docs/tickets/epic-<epic>-<slug>/`, for example `epic-2-transactions/`.
- Story file: `story-<epic>.<story>-<slug>.md` in the epic's `tickets/`, for example `story-2.3-linked-customer-lists-their-own-transactions.md`.
- The story ID is `<epic>.<story>` (`2.3`). Use it everywhere a story is named: lock tags, reports, `docs/verification/<story ID>-*.md`, commit messages.

## Dependency verification

Before adding any package, confirm it exists, is the real one (publisher, download count, repository) and is the latest stable version. Pin it exactly. Hallucinated package names are a supply-chain risk.

## When stuck

Never weaken a test to make it pass. After two failed attempts, or before anything destructive or irreversible, stop and return `blocked` with the reason. Don't ask for permission mid-task; report.

## Status contract

Every pipeline agent ends with one JSON object:

```json
{"status": "built", "assumptions": [], "open_questions": [], "conflicts": []}
```

Builders add `"criteria": [{"id": "...", "test": "..."}]` mapping each acceptance criterion to the test that proves it, `"standards": [{"mechanism": "...", "source": "<link>"}]` naming the documented approach or standard each new mechanism follows, `"breaks"` listing each deliberate break as a patch with the test it must make fail, and `"test_results"` naming the results files of the one full test run with the diff hash they cover. Builders also write the object to `docs/verification/<story>-build.json` in the story worktree, beside the results files, where the verifier reads it.

The reviewer, the security agent and the verifier add `"record"`, the `docs/verification/<story>-*.md` file they wrote, and `"findings"`, each ranked high, medium or low.

## Finish checklist

- Tests prove every acceptance criterion; the report maps criterion to test.
- The developer smoke check passed.
- No new suppressions, no new packages without verification.
- Every new mechanism follows a cited standard; any custom mechanism says why no standard fits.
- No forbidden terms.
- The report lists assumptions and open questions.

# Delivery process

## Roles

A supervisor session plans, dispatches, tracks and merges. It never writes production or test code; where the tool supports hooks a guard enforces this. Builders, the verifier, security, the reviewer and QA each work one story through the story flow below. Gates require human approval.

## Commits

Nothing is committed, pushed or merged without explicit approval for that change, with one standing approval (2026-10-29): a story below the high-stakes tier is committed and pushed once every stage passes, through `scripts/push-and-watch.sh`, which fails unless CI and Security succeed. A high-stakes story waits for the human's diff review, done while the next story builds; from 2026-10-30 the human waives that wait: every story, any tier, and any Small-tier fix with production code are committed once every stage passes and CI is green, and are reported after. The same standing approval covers a Small-tier fix outside a story that changes no production code (a test or tooling fix) once its checks pass; it is reported after. It also covers a docs-only commit that records the human's decisions, stories readied through the pipeline and pipeline records; a change to the rules or the process (AGENTS.md, CLAUDE.md, the skills) still asks. Otherwise stage the change, show what goes in, wait.

**Branch first, then main.** Main never takes an unverified push.

- A story is committed on its own branch, `story/<story>`, and pushed with `scripts/push-and-watch.sh`, which waits for the CI and Security workflows on the pushed commit. Any other change (a fix, a docs-only commit) goes through a short-lived branch the same way.
- The branch merges to main by fast-forward only, after a rebase onto main, once CI and Security are green on the branch head. A fast-forward puts on main the exact commit CI tested; a squash or a merge commit would put an untested one there, so neither is used. When the rebase changes the commit, the branch is pushed again (`--force-with-lease`, story branches only; main is never force-pushed) and waits for green again before the merge.
- A story that depends on an unmerged story builds on that story's branch (stacked), not on main, and rebases onto main once the base story merges.
- After the fast-forward, `scripts/push-and-watch.sh` on main confirms main's run, which also publishes the images.

## Story flow

```
pre-build review (Feature, High-stakes) → builder (self-check, one full test run, breaks) → reviewer ∥ verifier ∥ security (High-stakes only)
  → at most two fix rounds (high and medium findings) → QA (journey, HTTP, data or event stories) → branch CI green → fast-forward to main
```

**Pre-build review.** For a Feature or High-stakes story, one reviewer reads the story as a design against the architecture and the code, with the review and security checklists, and writes `docs/verification/<story>-prebuild-review.md`, tagging each point `[story]` or `[decision]`. `[story]` points (a bound, a missing criterion, a gap against the code) go to the product owner (Sofia), who fixes the story with no human gate; `[decision]` points contradict a PRD requirement or an architecture decision, so a human decides them, batched once per epic in `docs/verification/epic-<epic>-decisions.md`, before the stories they touch build. A Small story has no pre-build review. The builder runs the review and security checklists on its own diff before reporting.

**Decision-time challenge.** The challenger (`scripts/challenge-with-copilot.sh`, a different model family from the builders) runs in decision mode only, never per story. Before stories are written from a new or changed architecture decision, a PRD change or a brief, the supervisor runs it on that document (`scripts/challenge-with-copilot.sh <id> <file> decision`), without the supervisor's evaluation, and brings its findings to the human beside the supervisor's own.

**Post-build review.** The moment a build lands, the reviewer and the verifier start together, with the security agent beside them on a High-stakes story. The reviewer checks correctness, and runs the security checklist on a story below High-stakes. The verifier, a different model family, reads the builder's test results file instead of re-running the suite, runs the builder's `breaks` list, and reports findings. Each writes its own record (`docs/verification/<story>-review.md`, `-verify.md`, `-security.md`) in the main checkout, so a fix round's fresh worktree does not lose it; the supervisor reads the records and never transcribes them. A finding is a correctness or security gap, or a breach of a written rule, ranked high, medium or low; style and taste are not findings.

**QA** runs last, once the high and medium findings are fixed, and only for a story that changes a user journey, the HTTP surface, data or events. QA runs no ZAP scan: CI's ZAP baseline runs on every push. QA holds the `stack` lock for its own run only; no manual pass holds the stack.

For high-stakes stories the human review of the diff (Stakes tiers) happens in the review blocks, after the merge under the 2026-10-30 waiver (Commits).

- **One supervisor session per story.** Start from `docs/tickets/README.md`, the story file and its `docs/verification/<story>-*.md`; hand over in files, not in the conversation; clear the session after the merge and the capture below.
- **Fix rounds.** Only high and medium findings open a fix round. Collect every stage's high and medium findings for one commit into `docs/verification/<story>-fixes.md` and give them to one fresh builder, not a resumed one; `scripts/story-patch.sh` carries the story's uncommitted change to that builder (`docs/process/agentic-sdlc.md`). At most two fix rounds per story; a story that still has a high or medium finding after the second goes to the human. Low findings never open a round: at merge the supervisor adds them to `docs/verification/epic-<epic>-lows.md` (story, record, finding), and they are fixed in one batch per epic.
- **Re-runs after a fix.** The verifier always re-runs, on the fix builder's new results file and breaks; the reviewer reviews only the delta; security re-runs when the delta touches auth, identity, customer data or security headers; QA re-runs when the delta changes a user journey, the HTTP surface, data or events. Branch CI stays the full authoritative run.
- **One test run per commit.** The builder's full run, kept as its test results file, is the authoritative local result for its diff; the verifier and later stages read it and re-run only after a change.
- **Research.** One agent per question, with a scope and an output file; parallel audits split by file set with one merge owner; no fan-out to more models than the verifier and its backup.
- **A rule change mid-story** applies from the next story unless the human decides to rework the current one.

Stories run in the order of [docs/tickets/README.md](docs/tickets/README.md); `<story>` is the story ID `<epic>.<story>` (Ticket numbering). Keep the index current when a story merges or the plan changes.

Merge only when every stage passes and CI and Security are green on the story branch (Commits).

## Dispatch rules

- Four builder lanes: two machines (M1 and M2), two lanes each. Per machine, one Testcontainers slot, one verifier story at a time, one QA run at a time behind `scripts/test-lock.sh`.
- Sized for a build machine with 16 GB RAM, 12 cores and the Docker VM capped at 8 GB. A backend lane peaks near 2 GB on the host (build plus the full suite); the Compose stack can take up to 5.8 GB of the VM, so only one stack runs at a time.
- No lane sits idle while a ready story exists: the supervisor dispatches the next ready story the moment a lane frees. Disk gate: a lane starts only with at least 15 GB free on its machine (`scripts/lane.sh start` checks). Memory gate (`sysctl -n kern.memorystatus_vm_pressure_level`): at `1` (normal) dispatch freely within the lane limits; at `2` a machine with no builder lane running may start one (one lane, the suite run once); at `4` (critical) wait for a lane to finish. Review stages (reviewer, security, challenger, verifier) cost little locally and may run beside a builder.
- Stop the Compose stack (`docker compose stop`) whenever no one holds the `stack` lock.
- The story (a vertical slice) is the parallel unit. Start sequentially (platform baseline, then the tracer bullet), run the middle in parallel, finish with a sequential refactor sweep.
- Collision check before dispatching two stories together: no shared section of a file (a shared file is allowed when the edits are in different sections; the story that merges second rebases and regenerates `openapi.json` and `frontend/src/api/generated/`), never two migrations at once. A story that depends on another unmerged story is not held: it builds stacked on that story's branch (Commits).
- Frontend work starts once the API contract (`openapi.json`) is merged, or stacked on the story branch that changes it.
- The reviewer, the security agent and the verifier write their own records; the supervisor reads them and writes only the fix list, the epic's lows and the capture.
- The builder scripts each deliberate break in its `breaks` list; the verifier runs the list in a throwaway copy and records the result in `<story>-verify.md`. A Docker check the story names is QA's, or branch CI's where CI runs it; the supervisor reproduces nothing by hand.

## Continuous improvement

Agents learn only from what is written down where the right agent loads it.

- **After every merge (capture).** Add an Escapes table to `<story>-review.md`: each finding a later stage caught that an earlier stage should have, with the stage that caught it, the stage that should have, a category (spec gap, input boundary, non-standard mechanism, missing test, environment or tooling, flaky, script error, auth) and the countermeasure. Add one metrics row: fix rounds per stage, full-suite runs, wall time per stage, tokens and peak context per agent, disk change.
- **After every epic (retrospective).** Aggregate the epic's escapes and metrics, re-run the simplicity and standards audit with the same six file sets as `docs/verification/simplicity-audit/` and compare the counts, propose countermeasures to the human as one batch, and prune stale skill lines.
- **Immediately (trigger).** A recurring escape category, a second fix round, a stage failing for tooling (account, lock, disk), a full pipeline re-run, a supervisor context over 200k tokens, or free disk under 15 GB: write a short postmortem and a fix proposal at once.
- **A repeated mistake becomes a check, not a sentence.** Countermeasures in this order: a deterministic check (guard, analyzer, architecture test, floor-guard, script exit code); one line in the responsible agent's skill, under "Known pitfalls", at most about 10 lines, deleted once a check covers it; a field in the story template. Never in AGENTS.md or a tool's instruction file.
- **Machine hygiene.** Once a story is completely done, remove every worktree and copy it used on both machines (builder and fix-round worktrees, QA, verification and review copies, lane files) and their branches, even when they hold uncommitted copies of the merged change; weekly, or under 15 GB free, prune only DisputeDesk-scoped Docker leftovers (the Docker daemon is shared with other projects).

## Human gates

Escalate for a human decision only for: spec ambiguity or a re-plan, two failed fix attempts, irreversible actions, rule changes, high-severity security findings, model disagreement (a challenger `[decision]` point or any decision-mode finding), and the token-usage threshold.

## Planning

Planning runs through the BMAD method (brief, PRD, UX, architecture, the product owner Sofia), configured in `_bmad/`; decisions are grilled with the `grill` procedure before a brief or PRD. See [docs/process/agentic-sdlc.md](docs/process/agentic-sdlc.md).
