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
- the challenger's verdict is "proceed", or its `[story]` points are fixed in the story and a human has decided on its `[decision]` points (`docs/verification/<story>-challenge.md`).

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

Builders add `"criteria": [{"id": "...", "test": "..."}]` mapping each acceptance criterion to the test that proves it, and `"standards": [{"mechanism": "...", "source": "<link>"}]` naming the documented approach or standard each new mechanism follows.

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

Nothing is committed, pushed or merged without explicit approval for that change, with one standing approval (2026-10-29): a story below the high-stakes tier is committed and pushed once every stage passes, through `scripts/push-and-watch.sh`, which fails unless CI and Security succeed. A high-stakes story waits for the human's diff review, done while the next story builds. Otherwise stage the change, show what goes in, wait.

## Story flow

```
challenger ∥ pre-build review → builder (self-check) → verifier ∥ security (high-stakes only) ∥ reviewer → one fix round → QA → merge
```

The challenger (`scripts/challenge-with-copilot.sh`, a different model family from the builders) reads only the story, the architecture and the code it would touch, never the supervisor's view, and answers proceed, simplify or rethink, tagging each point `[story]` or `[decision]`. `[story]` points (a bound, a missing criterion, a gap against the code) go to the product owner (Sofia), who fixes the story with no human gate; `[decision]` points contradict a PRD requirement or an architecture decision, so they are a model disagreement and a human decides before the builder starts.

**Pre-build review.** Beside the challenger, the reviewer reads the story as a design against the architecture and the code, and security reads it against its checklist (`docs/verification/<story>-prebuild-review.md`, `-prebuild-security.md`); their `[story]` points go to Sofia with the challenger's, and `[decision]` points to the human. The builder runs the review and security checklists on its own diff before reporting.

**Decision-time challenge.** Before stories are written from a new or changed architecture decision, a PRD change or a brief, the supervisor runs the challenger in decision mode on that document (`scripts/challenge-with-copilot.sh <id> <file> decision`), without the supervisor's evaluation, and brings its findings to the human beside the supervisor's own. The verifier, security and the reviewer start together the moment a build lands, and their findings make one fix round; QA, the most expensive stage, runs last, once those findings are fixed. For high-stakes stories the human review of the diff (Stakes tiers) happens in the review blocks, before the commit is approved.

- **One supervisor session per story.** Start from `docs/tickets/README.md`, the story file and its `docs/verification/<story>-*.md`; hand over in files, not in the conversation; clear the session after the merge and the capture below.
- **Fix rounds.** Collect every stage's findings for one commit into `docs/verification/<story>-fixes.md` and give them to one fresh builder, not a resumed one; `scripts/story-patch.sh` carries the story's uncommitted change to that builder (`docs/process/agentic-sdlc.md`). At most one fix round per stage pass.
- **Re-runs after a fix.** The verifier always re-runs; the reviewer reviews only the delta; security re-runs when the delta touches auth, identity, customer data or security headers; QA re-runs when the HTTP surface, data or events change. CI stays the full authoritative run.
- **One test run per commit.** The builder's full run is the authoritative result for its diff; later stages reuse it and re-run only after a change.
- **Research.** One agent per question, with a scope and an output file; parallel audits split by file set with one merge owner; no fan-out to more models than the verifier and its backup.
- **A rule change mid-story** applies from the next story unless the human decides to rework the current one.

Stories run in the order of [docs/tickets/README.md](docs/tickets/README.md); `<story>` is the story ID `<epic>.<story>` (Ticket numbering). Keep the index current when a story merges or the plan changes.

Merge only when every stage passes and CI is green.

## Dispatch rules

- Two builder lanes at most per machine, one Testcontainers slot, one verifier story at a time, one QA run at a time behind `scripts/test-lock.sh`.
- Sized for a build machine with 16 GB RAM, 12 cores and the Docker VM capped at 8 GB. A backend lane peaks near 2 GB on the host (build plus the full suite); the Compose stack can take up to 5.8 GB of the VM, so only one stack runs at a time.
- No machine sits idle while a ready story exists. Memory gate (`sysctl -n kern.memorystatus_vm_pressure_level`): at `1` (normal) dispatch freely within the lane limits; at `2` a machine with no builder lane running may start one (one lane, the suite run once); at `4` (critical) wait for a lane to finish. Read-only stages (reviewer, security, challenger, verifier) cost little locally and may run beside a builder.
- Stop the Compose stack (`docker compose stop`) whenever no one holds the `stack` lock.
- The story (a vertical slice) is the parallel unit. Start sequentially (platform baseline, then the tracer bullet), run the middle in parallel, finish with a sequential refactor sweep.
- Collision check before dispatching two stories together: no shared files, never two migrations at once.
- Frontend work starts once the API contract (`openapi.json`) is merged.
- The supervisor reads reviewer and security findings directly and writes them to `docs/verification/<story>-review.md` and `<story>-security.md`.
- Story files given to the verifier hold only criteria it can check read-only. The supervisor reproduces the builder's deliberate breaks and any Docker checks in a throwaway copy, and records the result in `<story>-review.md`.

## Continuous improvement

Agents learn only from what is written down where the right agent loads it.

- **After every merge (capture).** Add an Escapes table to `<story>-review.md`: each finding a later stage caught that an earlier stage should have, with the stage that caught it, the stage that should have, a category (spec gap, input boundary, non-standard mechanism, missing test, environment or tooling, flaky, script error, auth) and the countermeasure. Add one metrics row: fix rounds per stage, full-suite runs, wall time per stage, tokens and peak context per agent, disk change.
- **After every epic (retrospective).** Aggregate the epic's escapes and metrics, propose countermeasures to the human as one batch, and prune stale skill lines.
- **Immediately (trigger).** A recurring escape category, a second fix round, a stage failing for tooling (account, lock, disk), a full pipeline re-run, a supervisor context over 200k tokens, or free disk under 15 GB: write a short postmortem and a fix proposal at once.
- **A repeated mistake becomes a check, not a sentence.** Countermeasures in this order: a deterministic check (guard, analyzer, architecture test, floor-guard, script exit code); one line in the responsible agent's skill, under "Known pitfalls", at most about 10 lines, deleted once a check covers it; a field in the story template. Never in AGENTS.md or a tool's instruction file.
- **Machine hygiene.** After every merge, remove merged clean worktrees and their branches and the session's throwaway copies; weekly, or under 15 GB free, prune only DisputeDesk-scoped Docker leftovers (the Docker daemon is shared with other projects).

## Human gates

Escalate for a human decision only for: spec ambiguity or a re-plan, two failed fix attempts, irreversible actions, rule changes, high-severity security findings, model disagreement (a challenger `[decision]` point or any decision-mode finding), and the token-usage threshold.

## Planning

Planning runs through the BMAD method (brief, PRD, UX, architecture, the product owner Sofia), configured in `_bmad/`; decisions are grilled with the `grill` procedure before a brief or PRD. See [docs/process/agentic-sdlc.md](docs/process/agentic-sdlc.md).
