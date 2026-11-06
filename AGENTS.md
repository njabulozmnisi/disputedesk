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
| Feature | a feature spec | The full story flow: builder, verifier, QA, reviewer |
| High-stakes | auth, identity and account linking, customer data, money, migrations | The Feature tier's flow plus the security agent and a human review of the diff |

## Definition of ready

A story is ready to build only when:

- every criterion is testable and tagged to a requirement;
- each criterion names the mechanism that implements it and the documented source for it (Microsoft Learn, the RFC, OWASP);
- every request input is listed with its bounds;
- it has no open questions;
- it has at most about 8 acceptance criteria; a larger story is split into vertical slices;
- a Feature-tier or High-stakes story has had its pre-build review (`docs/verification/<id>-prebuild-review.md`): its `[story]` points are fixed in the story and a human has decided on its `[decision]` points (`docs/verification/decisions.md`).

## Ticket numbering

New work is a feature spec: a BMAD backlog story, `docs/backlog/story-<id>.md`, named by its feature slug, with no epic and no number. The `## Features` table of [docs/tickets/README.md](docs/tickets/README.md) lists the feature specs in build order.

- The feature slug matches `^[a-z][a-z0-9-]{0,40}$`, does not start with `records-` or `lows-` (reserved for the records and lows pull requests), and never equals another slug followed by `-` and more characters. It is the narrower form of the Branches `<slug>` bound (Commits).
- Use the slug everywhere a feature spec is named: lock tags, reports, `docs/verification/<id>-*.md`, commit messages.
- The numbered epics and stories listed in docs/tickets/README.md are done and keep their files, IDs and records.

## Dependency verification

Before adding any package, confirm it exists, is the real one (publisher, download count, repository) and is the latest stable version. Pin it exactly. Hallucinated package names are a supply-chain risk.

## When stuck

Never weaken a test to make it pass. After two failed attempts, or before anything destructive or irreversible, stop and return `blocked` with the reason. Don't ask for permission mid-task; report.

## Status contract

Every pipeline agent ends with one JSON object:

```json
{"status": "built", "assumptions": [], "open_questions": [], "conflicts": []}
```

Builders add `"criteria": [{"id": "...", "test": "..."}]` mapping each acceptance criterion to the test that proves it, `"standards": [{"mechanism": "...", "source": "<link>"}]` naming the documented approach or standard each new mechanism follows, `"breaks"` listing each deliberate break as a patch with the test it must make fail, and `"test_results"` naming the results files of the one full test run with the diff hash they cover. Builders also write the object to `docs/verification/<id>-build.json` in the story worktree, beside the results files, where the verifier reads it.

The reviewer, the security agent and the verifier add `"record"`, the `docs/verification/<id>-*.md` file they wrote, and `"findings"`, each ranked high, medium or low.

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

Nothing is committed, pushed or merged without explicit approval for that change, with one standing approval (2026-10-29): a story below the high-stakes tier is committed and pushed once every stage passes, and merged through its pull request by the steps below, which merge it only once CI and Security pass. A high-stakes story waits for the human's diff review, done while the next story builds; from 2026-10-30 the human waives that wait: every story, any tier, and any Small-tier fix with production code are committed once every stage passes and CI is green, and are reported after. The same standing approval covers a Small-tier fix outside a story that changes no production code (a test or tooling fix) once its checks pass; it is reported after. It also covers a docs-only commit that records the human's decisions, stories readied through the pipeline and pipeline records; a change to the rules or the process (AGENTS.md, CLAUDE.md, the skills) still asks. Otherwise stage the change, show what goes in, wait.

**Pull request first, then main.** Every change reaches main only through its pull request; main's ruleset requires one, with the `ci-passed` and `security-passed` checks and code scanning passing on a branch that is up to date with main, and it refuses force pushes and deletion (AD-32).

- **Branches.** A story is built and committed on `feature/<id>`. Any other change (a records change, a Small fix, a docs-only change) goes on `feature/<slug>` (`<slug>` matching `^[a-z][a-z0-9.-]{0,49}$`). Both take the same steps and wait for the full checks: every check job, the e2e journeys and the ZAP baseline included, runs on every pull request and on main.
- **Steps** (the commands are in `docs/process/agentic-sdlc.md`, section 2). Push the branch; open its pull request against main, or against `feature/<base>` for a stacked story, titled with the commit-message subject (one line, at most 72 characters) and with the detail in the body, which becomes the squash commit's message; wait for its required checks. Only once every stage has passed and the base reads main, arm squash auto-merge pinned to the head whose tree the stages checked (`--match-head-commit`); auto-merge never replaces a stage. Before any later push to an armed pull request, disable auto-merge, and re-arm on the new head once its checks pass. Then confirm the merge and its squash commit, and wait for main's CI and Security runs on that commit. Every `gh` write and read-back runs as the repository owner (`GH_TOKEN="$(gh auth token --user <owner>)" gh …`); the token is never printed, logged or written to a file.
- **What main gets.** Main takes only a squash merge. The pull request's checks test GitHub's merge of the up-to-date branch into main, and main's own run builds, scans and publishes the squash commit (ad-012). A pull request that is only behind main is updated with `gh pr update-branch`, never a force push; one with a conflict, or a drift check that fails after an update, goes to a builder as a fix round, since the supervisor writes no code.
- **Stacked stories.** A story that depends on an unmerged story builds on that story's branch, `feature/<base>`, and opens its pull request against it. When the base squash-merges, GitHub deletes `feature/<base>` and retargets the stacked pull request to main; the stacked branch is rebased onto main (`git rebase --onto origin/main <base's last head> feature/<id>`), pushed with the one lease-checked force push, `git [-C <path>] push --force-with-lease --force-if-includes origin feature/<id>`, and its checks run again before any merge. No other force push is made; main is never force-pushed, and the ruleset refuses it.
- **Records.** The records of a merged story (the capture, its rows in `docs/verification/lows.md` and `docs/verification/decisions.md`, a stage's read-back record, and its Features row in `docs/tickets/README.md` set to `done`) ride in one records pull request, `feature/records-<id>`, after its merge.

## Story flow

```
pre-build review (Feature tier, High-stakes) → builder (self-check, one full test run, breaks) → reviewer ∥ verifier ∥ security (High-stakes only)
  → at most two fix rounds (high and medium findings) → QA (journey, HTTP, data or event stories) → pull request checks green → squash merge to main → main's run green
```

**Pre-build review.** For a Feature-tier or High-stakes story, one reviewer reads the story as a design against the architecture and the code, with the review and security checklists, and writes `docs/verification/<id>-prebuild-review.md`, tagging each point `[story]` or `[decision]`. `[story]` points (a bound, a missing criterion, a gap against the code) go to the product owner (Sofia), who fixes the story with no human gate; `[decision]` points contradict a PRD requirement or an architecture decision, so a human decides them, recorded in `docs/verification/decisions.md` (feature, point, answer, date), before the stories they touch build. A Small story has no pre-build review. The builder runs the review and security checklists on its own diff before reporting.

**Decision-time challenge.** The challenger (`scripts/challenge-with-copilot.sh`, a different model family from the builders) runs in decision mode only, never per story. Before stories are written from a new or changed architecture decision, a PRD change or a brief, the supervisor runs it on that document (`scripts/challenge-with-copilot.sh <id> <file> decision`), without the supervisor's evaluation, and brings its findings to the human beside the supervisor's own.

**Post-build review.** The moment a build lands, the reviewer and the verifier start together, with the security agent beside them on a High-stakes story. The reviewer checks correctness, and runs the security checklist on a story below High-stakes. The verifier, a different model family, reads the builder's test results file instead of re-running the suite, runs the builder's `breaks` list, and reports findings. Each writes its own record (`docs/verification/<id>-review.md`, `-verify.md`, `-security.md`) in the main checkout, so a fix round's fresh worktree does not lose it; the supervisor reads the records and never transcribes them. A finding is a correctness or security gap, or a breach of a written rule, ranked high, medium or low; style and taste are not findings.

**QA** runs last, once the high and medium findings are fixed, and only for a story that changes a user journey, the HTTP surface, data or events. QA runs no ZAP scan: CI's ZAP baseline runs on every pull request and on main. QA holds the `stack` lock for its own run only; no manual pass holds the stack.

For high-stakes stories the human review of the diff (Stakes tiers) happens in the review blocks, after the merge under the 2026-10-30 waiver (Commits).

- **One supervisor session per story.** Start from `docs/tickets/README.md`, the feature spec and its `docs/verification/<id>-*.md`; hand over in files, not in the conversation; clear the session after the merge and the capture below.
- **Fix rounds.** Only high and medium findings open a fix round. Collect every stage's high and medium findings for one commit into `docs/verification/<id>-fixes.md` and give them to one fresh builder, not a resumed one; `scripts/story-patch.sh` carries the story's uncommitted change to that builder (`docs/process/agentic-sdlc.md`). At most two fix rounds per story; a story that still has a high or medium finding after the second goes to the human. Low findings never open a round: at merge the supervisor adds them to `docs/verification/lows.md` (feature, record, finding, closed), and they are fixed in the lows batch (Continuous improvement).
- **Re-runs after a fix.** The verifier always re-runs, on the fix builder's new results file and breaks; the reviewer reviews only the delta; security re-runs when the delta touches auth, identity, customer data or security headers; QA re-runs when the delta changes a user journey, the HTTP surface, data or events. The pull request's checks stay the full authoritative run.
- **One test run per commit.** The builder's full run, kept as its test results file, is the authoritative local result for its diff; the verifier and later stages read it and re-run only after a change.
- **Research.** One agent per question, with a scope and an output file; parallel audits split by file set with one merge owner; no fan-out to more models than the verifier and its backup.
- **A rule change mid-story** applies from the next story unless the human decides to rework the current one.

Stories run in the order of [docs/tickets/README.md](docs/tickets/README.md); `<id>` is the feature slug (Ticket numbering). Keep the index current when a story merges or the plan changes.

Merge only when every stage passes and CI and Security are green on the story's pull request (Commits).

## Dispatch rules

- Four builder lanes: two machines (M1 and M2), two lanes each. Per machine, one Testcontainers slot, one verifier story at a time, one QA run at a time behind `scripts/test-lock.sh`.
- Sized for a build machine with 16 GB RAM, 12 cores and the Docker VM capped at 8 GB. A backend lane peaks near 2 GB on the host (build plus the full suite); the Compose stack's memory limits total about 2.3 GB of the VM (`docker-compose.yml`), so only one stack runs at a time.
- No lane sits idle while a ready story exists: the supervisor dispatches the next ready story the moment a lane frees. Disk gate: a lane starts only with at least 15 GB free on its machine (`scripts/lane.sh start` checks). Memory gate (`sysctl -n kern.memorystatus_vm_pressure_level`): at `1` (normal) dispatch freely within the lane limits; at `2` a machine with no builder lane running may start one (one lane, the suite run once); at `4` (critical) wait for a lane to finish. Review stages (reviewer, security, challenger, verifier) cost little locally and may run beside a builder.
- Stop the Compose stack (`docker compose stop`) whenever no one holds the `stack` lock.
- The story (a vertical slice) is the parallel unit. Start sequentially (platform baseline, then the tracer bullet), run the middle in parallel, finish with a sequential refactor sweep.
- Collision check before dispatching two stories together: no shared section of a file (a shared file is allowed when the edits are in different sections; the story that merges second rebases and regenerates `openapi.json` and `frontend/src/api/generated/`), never two migrations at once. A story that depends on another unmerged story is not held: it builds stacked on that story's branch (Commits).
- Frontend work starts once the API contract (`openapi.json`) is merged, or stacked on the story branch that changes it.
- The reviewer, the security agent and the verifier write their own records; the supervisor reads them and writes only the fix list, the lows rows and the capture.
- The builder scripts each deliberate break in its `breaks` list; the verifier runs the list in a throwaway copy and records the result in `<id>-verify.md`. A Docker check the story names is QA's, or the pull request's CI where CI runs it; the supervisor reproduces nothing by hand.

## Continuous improvement

Agents learn only from what is written down where the right agent loads it.

- **After every merge (capture).** Add an Escapes table to `<id>-review.md`: each finding a later stage caught that an earlier stage should have, with the stage that caught it, the stage that should have, a category (spec gap, input boundary, non-standard mechanism, missing test, environment or tooling, flaky, script error, auth) and the countermeasure. Add one metrics row: fix rounds per stage, full-suite runs, wall time per stage, tokens and peak context per agent, disk change.
- **Lows batch and retrospective.** When `docs/verification/lows.md` holds 10 open rows or five feature specs have merged since the last batch, whichever comes first, the open lows are fixed in one `feature/lows-<yyyy-mm-dd>` pull request with the retrospective beside it. The retrospective aggregates the escapes and metrics since the last batch, re-runs a simplicity and standards audit over the same six areas (backend, frontend, e2e, infra, scripts and CI, docs) and compares the counts, proposes countermeasures to the human as one batch, and prunes stale skill lines.
- **Immediately (trigger).** A recurring escape category, a second fix round, a stage failing for tooling (account, lock, disk), a full pipeline re-run, a supervisor context over 200k tokens, or free disk under 15 GB: write a short postmortem and a fix proposal at once.
- **A repeated mistake becomes a check, not a sentence.** Countermeasures in this order: a deterministic check (guard, analyzer, architecture test, floor-guard, script exit code); one line in the responsible agent's skill, under "Known pitfalls", at most about 10 lines, deleted once a check covers it; a field in the story template. Never in AGENTS.md or a tool's instruction file.
- **Machine hygiene.** Once a story is completely done, remove every worktree and copy it used on both machines (builder and fix-round worktrees, QA, verification and review copies, lane files) and their branches, even when they hold uncommitted copies of the merged change; weekly, or under 15 GB free, prune only DisputeDesk-scoped Docker leftovers (the Docker daemon is shared with other projects).

## Human gates

Escalate for a human decision only for: spec ambiguity or a re-plan, two failed fix attempts, irreversible actions, rule changes, high-severity security findings, model disagreement (a challenger `[decision]` point or any decision-mode finding), and the token-usage threshold.

## Planning

Planning runs through the BMAD method (brief, PRD, UX, architecture, the product owner Sofia), configured in `_bmad/`; decisions are grilled with the `grill` procedure before a brief or PRD. See [docs/process/agentic-sdlc.md](docs/process/agentic-sdlc.md).
