# Agent-assisted SDLC

How DisputeDesk is planned, built, verified and shipped with AI agents, and where the human stays in control. The multi-agent layer is optional: everything here can also be done by a person with any AI assistant, following [AGENTS.md](../../AGENTS.md).

## 1. The lifecycle

```mermaid
flowchart TB
    classDef human fill:#fde68a,stroke:#b45309,color:#111
    classDef gate fill:#fca5a5,stroke:#b91c1c,color:#111
    classDef skill fill:#bfdbfe,stroke:#1d4ed8,color:#111
    classDef agent fill:#bbf7d0,stroke:#15803d,color:#111
    classDef artifact fill:#f3f4f6,stroke:#6b7280,color:#111

    N([Human approval: architecture and product decisions]):::human

    subgraph P1["Phase 1: Plan (BMAD skills, human in the loop)"]
        G[grill: batched rounds, recommended answers]:::skill --> GL[(docs/glossary.md, decision records)]:::artifact
        G --> R[bmad-deep-recon: dispute lifecycle, event versioning]:::skill
        R --> B[bmad-product-brief]:::skill
        B --> PRD[bmad-prd + bmad-review adversarial, edge cases]:::skill
        PRD --> S1[Sofia: initiative ticket, R-n lines citing FR-n]:::skill
        S1 --> UX[bmad-ux + clickable mockups]:::skill
        UX --> A[bmad-architecture: AD-n spine, data model]:::skill
    end

    A --> G1{Gate 1: PRD, UX, architecture}:::gate
    G1 --> S2[Sofia: slice epics and stories, tracer bullet first]:::skill
    S2 --> RC[Readiness: Sofia validate + bmad-review on the spine]:::skill
    RC --> G2{Gate 2: slicing}:::gate

    subgraph P0["Phase 0: Platform (runs alongside Phase 1)"]
        P0a[Skeleton, CI gates, floor guard, Compose healthchecks, OpenTelemetry baseline]:::agent
    end

    G2 --> P3[[Phase 3: story pipeline, see section 2]]
    P0 --> P3
    P3 --> P4[Phase 4: AWS deploy, docs, fresh-clone test, rehearsal]:::agent
    P4 --> SUB([Submit]):::human

    N -.answers rounds.-> G
    N -.approves.-> G1
    N -.approves.-> G2
```

## 2. One story through the pipeline

The main Claude Code session is the supervisor: it dispatches, tracks and merges, and never writes code.

```mermaid
sequenceDiagram
    autonumber
    actor N as Human approver
    participant S as Supervisor (main session)
    participant B as Builder (Opus backend / Sonnet frontend)
    participant V as Verifier (Sonnet → GPT-6 Astra via Copilot CLI)
    participant SEC as Security (Opus, writes only its record)
    participant Q as QA (Sonnet + Playwright MCP)
    participant R as Reviewer (Opus, writes only its record)

    S->>S: check-ready, collision check (stacked on story/<base> when it depends on an unmerged story)
    S->>B: Story from docs/tickets (worktree from main, or from story/<base>)
    B->>B: Test first, all checks, one full run with a results file, breaks list, developer smoke check
    B-->>S: {status: built, criteria → tests, breaks, test_results}, <id>-build.json in the worktree
    par Post-build review, records in docs/verification/
        S->>R: Correctness, plus the security checklist below High-stakes
        R-->>S: <id>-review.md, findings high / medium / low
    and
        S->>V: Read the results file, run the breaks list
        V->>V: verify-with-copilot.sh, run-breaks in a throwaway copy (testcontainers lock)
        V-->>S: <id>-verify.md keyed by SHA and diff hash, findings
    and High-stakes story only (auth, identity, customer data, money, migrations)
        S->>SEC: Security review
        SEC-->>S: <id>-security.md, findings
        SEC-->>N: High-severity findings escalated for a decision
    end
    opt High or medium findings (at most two rounds, then the human)
        S->>B: <id>-fixes.md to a fresh builder; the stages re-run on the delta
    end
    opt Story changes a user journey, the HTTP surface, data or events
        S->>Q: QA in that worktree
        Q->>Q: stack lock for this run only, walk every criterion: browser → network → DB (read-only role) → events
        Q-->>S: {status: pass|fail}, <id>-qa.md + committed Playwright tests
    end
    S->>S: Commit on story/<id>, push-and-watch.sh (CI + Security on the branch)
    S->>S: Rebase onto main, fast-forward main, push-and-watch.sh on main
    S-->>N: Report after the merge; High-stakes diffs in the review blocks
```

Before dispatch the supervisor runs `scripts/check-ready.sh <story>`, which fails while a `[story]` point in the pre-build review has no Sofia disposition, a `[decision]` point exists but the story's front matter lacks `decisions: settled` (the supervisor sets it, citing the epic's `epic-<epic>-decisions.md`, once the human has answered), or a story in the `after` list is neither done nor pushed as a `story/<id>` branch.

Pre-build, one reviewer reads each Feature and High-stakes story with the review and security checklists; there is no per-story challenger. The challenger runs only in decision mode, on a new or changed architecture decision, PRD or brief.

Only high and medium findings send the story to a fix round. Lows go to `docs/verification/epic-<epic>-lows.md` and are fixed in one batch per epic. After two fix rounds a story that still has a high or medium finding goes to the human.

A fix round goes to one fresh builder, whose isolated worktree starts at the base commit, not at the story's uncommitted change. The supervisor runs `scripts/story-patch.sh <story-worktree> <base> <patch>`, which writes the change as one binary patch and fails unless the patch reproduces the worktree; the fix brief starts with `git apply --binary --exclude='docs/*' <patch>`, and the builder's worktree becomes the story worktree.

A fix item that moves a header, or the component that owns a behaviour, lists every response or path the criterion names, each with its expected value.

A lane on a worker machine runs through `scripts/lane.sh start|wait|collect <host> <story> ...`: it starts a headless builder in a tmux session, waits for its exit file, and brings back the report and a verified patch. Two machines (M1 and M2) run two lanes each; `lane.sh start` refuses a lane under 15 GB free, and the supervisor fills a lane the moment it frees.

The verifier does not re-run the suite. It checks that the builder's results files cover the current diff (the diff hash in `<story>-build.json`), judges each criterion against its test, and runs each `breaks` entry in a throwaway copy: apply the patch, run the named test, expect a failure. A break that still passes, or a results file for another diff, is a high finding.

Every push goes through `scripts/push-and-watch.sh`, which pushes the current branch and exits non-zero unless the CI and Security workflows succeed on the pushed commit. A story is pushed on `story/<id>`; once its run is green it is rebased onto main (pushed and watched again if the rebase changed the commit), main is fast-forwarded to it, and `push-and-watch.sh` runs once more on main. Main only ever receives a commit CI already passed. A story that depends on an unmerged one branches from `story/<base>` and rebases onto main after the base merges.

## 3. What enforces the rules

Prose rules get violated, so the critical ones are tooling.

```mermaid
flowchart LR
    classDef hook fill:#e9d5ff,stroke:#7e22ce,color:#111
    classDef ci fill:#bfdbfe,stroke:#1d4ed8,color:#111

    subgraph CC["Claude Code hooks (.claude/hooks)"]
        GU[guard.py, before every Bash / Write / Edit]:::hook
        SC[status_contract.py, when a pipeline agent stops]:::hook
    end
    subgraph GIT["Git hooks (.githooks)"]
        PC[pre-commit: gitleaks + forbidden terms]:::hook
    end
    subgraph CI["CI (Phase 0)"]
        CIx[build, tests, architecture tests, OpenAPI drift, floor guard, security scans]:::ci
        CId[docs-only push: floor guard, gitleaks, forbidden terms only]:::ci
    end

    GU --> G1[Commit, push, merge → explicit approval]
    GU --> G2[Each agent writes only its own area; the supervisor writes no code]
    GU --> G3[No hook bypass, force push, hard reset, prune]
    GU --> G4[No system clock, suppressions, DB mocks, banned packages in .NET code]
    GU --> G5[terraform apply/destroy and aws → explicit approval]
    GU --> G6[No forbidden terms anywhere]
    SC --> G7[Agent must end with the status contract]
    PC --> G8[No secrets or forbidden terms reach history]
```

A push that changes only Markdown and `docs/` skips CI's build, test, e2e and ZAP jobs; the floor guard, gitleaks and the forbidden-terms check still run on it. Every other push runs the full CI, the ZAP baseline included, so QA runs no ZAP scan of its own.

## Agents, skills and files

| Piece | Where |
|---|---|
| Rules for every assistant, delivery process included | `AGENTS.md` |
| Claude Code mechanics (guard hook, agents, skills, dispatch) | `CLAUDE.md` (plus the gitignored `CLAUDE.local.md`) |
| Agents | `.claude/agents/` — `backend-builder`, `frontend-builder`, `verifier`, `qa`, `reviewer`, `security` |
| Story procedures | `.claude/skills/` — `build-story`, `verify-story`, `qa-story`, `review-story`, `security-story`, `grill` |
| Planning skills | BMAD v7, pinned in `skills-lock.json`, restored by `scripts/bmad-install.sh` (not committed), configured in `_bmad/config.toml` and `_bmad/custom/` |
| Hooks and settings | `.claude/hooks/`, `.claude/settings.json` |
| MCP servers | `.mcp.json` — Microsoft Learn, Playwright |
| Shared-resource locks | `scripts/test-lock.sh` (`stack`, `testcontainers`) |
| Non-author verification | `scripts/verify-with-copilot.sh` |
| Machine setup | `scripts/bootstrap.sh` |
| Model choices | [ad-004](ad-004-model-roles.md) |
| Where the workflow comes from | [workflow-sources.md](workflow-sources.md) |
