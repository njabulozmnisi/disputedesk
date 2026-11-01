# Epic 7 retrospective

Epic 7 (journeys proven on the stack, deployed and documented), 2026-10-31 to 2026-11-01. Stories, in merge order: 7.1, 7.2, 7.3, 7.6, 7.11, 7.4, 7.7, 7.5, the e2e tooling batch, 7.10 (review), 7.12, 7.13, 7.14, then 7.10 closed. Escapes are in each `<story>-review.md`; second fix rounds in `postmortem-second-fix-rounds.md`; the dispatch-order postmortem in `postmortem-dispatch-order.md`.

## What the escapes say

About 43 escapes across 11 stories. By category:

| Category | Count (approx.) | Where it bit |
|---|---|---|
| Spec gap | 17 | 7.2 (pre-build points never folded), 7.4 (criterion needing an unmerged story), 7.12 (upstream headers, fix-list contradiction), 7.13 (unsigned ID tokens, 2 s vs AD-21), 7.14 (mutation cache, anonymous answer, criterion 3) |
| Missing test | 13 | 7.5 (vacuous Terraform assertions), 7.12 (Retry-After, error body), 7.14 (late-response guards, skip link per session state, memory not freed) |
| Input boundary | 5 | 7.4 (KMS alias), 7.5 (SHA not on main), 7.13 (read before refusal audit) |
| Environment or tooling | 5 | 7.3 and CI fixes (CI resolver and stopped-container behaviour), 7.5 (Trivy config scan never run before merge), registry timeout |
| Flaky | 3 | `RateLimitWindowTests`, the `FakeZitadel` token race (fixed), `linking-queue` margin |

The pattern: most escapes were not code mistakes but assumptions nobody executed: a story's claim about framework behaviour, a CI network that behaves differently, a cleared cache that still held memory. Every one was caught before merge except three CI-only failures (all caught by `push-and-watch.sh`, none reached a release).

## Metrics

| Story | Fix rounds | Notes |
|---|---|---|
| 7.1 | 1 | |
| 7.2 | 2 | postmortem (pre-build fold skipped) |
| 7.3 | 1 | two CI fixes after merge |
| 7.4 | 1 + rebase step | dispatched ahead of order |
| 7.5 | 1 | Security red after merge (Trivy config), triaged by human decision |
| 7.7 | 1 | fresh-clone check relaunched once |
| 7.12 | 2 | postmortem; CI fix after merge |
| 7.13 | 1 | flake traced to a 2.2 fixture |
| 7.14 | 3 build, 1 test follow-up, 3 QA | postmortem; two human gates |

Human decisions this epic: AWS sign-in (IAM user with MFA), e2e reset (more fresh stacks), Trivy findings (two accepted), the 7.10 disposition (70 findings), AD-23 and AD-24 amendments, P38 and P43 scope, and two 7.14 gates.

## Proposed countermeasures (for the human, one batch)

Ordered as AGENTS.md asks: a deterministic check first, then a skill line, then a template field.

1. **Readiness check script** (`scripts/check-ready.sh <story>`): fails when the story's `-challenge.md`, `-prebuild-review.md` or `-prebuild-security.md` holds a `[story]` point with no "Sofia, <date>" disposition line, or a `[decision]` with no human decision line. Run before dispatch. (7.2, 7.12.)
2. **Dispatch-order check** in the same script: fails when the story's `after` list names a story not marked done. (7.4.)
3. **Local security scan before reporting** (`scripts/security-local.sh`): runs `security.yml`'s `trivy fs --scanners vuln,misconfig` with the repo ignore file, so builders and security see a Terraform or Dockerfile finding before merge. One Known-pitfall line in build-story and security-story to run it. (7.5.)
4. **Memory regression rule**: any change that clears customer data in the SPA adds a `WeakRef` plus forced-GC test (`sessionEndCollected.test.tsx` is the pattern); one Known-pitfall line in build-story (frontend) and security-story: "a cleared cache is not freed memory". (7.14.)
5. **Every route, every session state**: the skip-link direct-load test in `routing.test.tsx` already loops over routes and sessions; one Known-pitfall line in review-story: a mechanism that wraps `<Outlet/>` is checked on every route with every session state. (7.14.)
6. **CI network parity**: one Known-pitfall line in build-story (platform): a health check covers every route it fronts, and no test relies on a stopped container refusing connections (use an upstream that closes the connection). (7.3, 7.12.)
7. **Pinned upstream behaviour**: one Known-pitfall line in review-story and security-story: read the pinned image's real headers or behaviour (for example a Next.js `routes-manifest.json`) and make test stand-ins send the same. (7.12.)
8. **Verifier tool access**: `scripts/verify-with-copilot.sh` allows `scripts/test-lock.sh run testcontainers` and `terraform test` in its tool list, so the verifier's sandbox stops reporting Testcontainers and Terraform as unrun. (7.5, 7.12, 7.14.)
9. **Supervisor fix-list rule** (one line in the supervisor's section of `docs/process/agentic-sdlc.md`): a fix item that changes which component sets a header or owns a behaviour lists every response or path the criterion names, with the expected value. (7.12.)

Stale skill lines to prune once the checks above exist: the build-story line about pre-build points (covered by item 1) and the review-story line on dispatch order (covered by item 2).

## Applied (human approval 2026-11-01, all nine)

- `scripts/check-ready.sh <story>` (items 1, 2): exit 0 ready, 1 not ready, 2 bad ID. Epic 7 passes except 7.11 (its `[story]` points have no Sofia disposition line, a real gap of the kind the check exists to catch). Older stories 2.6, 2.8, 3.4, 3.7 to 3.12, 4.2 and 6.1 fail because they record the disposition as "Product owner refine"; the rule was not widened for already-merged stories.
- `scripts/security-local.sh` (item 3): the Security workflow's Trivy filesystem and config scan, skipping `.claude/worktrees` and `node_modules`; clean on main.
- `scripts/verify-with-copilot.sh` (item 8): `terraform` allowed, with `terraform apply` and `terraform destroy` denied; `test-lock.sh` left out, because the script already holds the testcontainers lock for the whole run (a nested lock would wait out its timeout) and allowing it would let `run stack x -- <command>` bypass the deny list. The verifier can already run Testcontainers tests with plain `dotnet test`; where it still reports them unrun, the likely cause is Docker access inside its sandbox (unverified).
- Known-pitfall lines (items 3 to 7) in build-story (a new section, three lines), security-story (three lines) and review-story (two lines; the section is at 11 lines). The stale lines named for pruning did not exist.
- `docs/process/agentic-sdlc.md` (items 1 and 9): run `check-ready.sh` before dispatch; a fix item that moves a header or a behaviour's owner lists every response the criterion names.
