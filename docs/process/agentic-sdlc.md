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

    N([Njabulo: architect and product owner]):::human

    subgraph P1["Phase 1: Plan (BMAD skills, Njabulo in the loop)"]
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
    actor N as Njabulo
    participant S as Supervisor (main session)
    participant B as Builder (Opus backend / Sonnet frontend)
    participant V as Verifier (Sonnet → GPT-6 Astra via Copilot CLI)
    participant SEC as Security (Opus, read-only)
    participant Q as QA (Sonnet + Playwright MCP)
    participant R as Reviewer (Opus, read-only)

    S->>S: Collision check (no shared files, one migration at a time)
    S->>B: Story from docs/tickets (worktree from local HEAD)
    B->>B: Test first, all checks, developer smoke check
    B-->>S: {status: built, criteria → tests, worktree}
    S->>V: Verify in that worktree
    V->>V: verify-with-copilot.sh (testcontainers lock)
    V-->>S: {status: pass|fail}, docs/verification/<id>-verify.md keyed by SHA
    opt High-stakes story (auth, identity, customer data, money, migrations)
        S->>SEC: Security review
        SEC-->>S: {status: pass|findings}
        SEC-->>N: High-severity findings go straight to Njabulo
    end
    S->>Q: QA in that worktree
    Q->>Q: stack lock, walk every criterion: browser → network → DB (read-only role) → events, ZAP baseline
    Q-->>S: {status: pass|fail}, evidence + committed Playwright tests
    S->>R: Two-pass review: correctness, then simplicity
    R-->>S: {status: approve|changes}
    S->>N: Staged diff + all verdicts
    N-->>S: Explicit approval
    S->>S: Commit, merge to main (CI must be green)
    S->>S: Study note to ~/disputedesk-study, review-debt counter +1
```

A failed stage sends the story back to its builder with the findings. After two failed attempts the builder returns `blocked` and the supervisor asks Njabulo.

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
    end

    GU --> G1[Commit, push, merge → Njabulo approves]
    GU --> G2[Each agent writes only its own area; the supervisor writes no code]
    GU --> G3[No hook bypass, force push, hard reset, prune]
    GU --> G4[No system clock, suppressions, DB mocks, banned packages in .NET code]
    GU --> G5[terraform apply/destroy and aws → Njabulo]
    GU --> G6[No forbidden terms anywhere]
    SC --> G7[Agent must end with the status contract]
    PC --> G8[No secrets or forbidden terms reach history]
```

## Agents, skills and files

| Piece | Where |
|---|---|
| Rules for every assistant | `AGENTS.md` |
| Supervisor instructions | `CLAUDE.md` (plus the gitignored `CLAUDE.local.md`) |
| Agents | `.claude/agents/` — `backend-builder`, `frontend-builder`, `verifier`, `qa`, `reviewer`, `security` |
| Story procedures | `.claude/skills/` — `build-story`, `verify-story`, `qa-story`, `review-story`, `security-story`, `grill` |
| Planning skills | BMAD v7, pinned in `skills-lock.json`, restored by `scripts/bmad-install.sh` (not committed), configured in `_bmad/config.toml` and `_bmad/custom/` |
| Hooks and settings | `.claude/hooks/`, `.claude/settings.json` |
| MCP servers | `.mcp.json` — Microsoft Learn, Playwright |
| Shared-resource locks | `scripts/test-lock.sh` (`stack`, `testcontainers`) |
| Non-author verification | `scripts/verify-with-copilot.sh` |
| Machine setup | `scripts/bootstrap.sh` |
| Model choices | [ad-004](../decisions/ad-004-model-roles.md) |
| Where the workflow comes from | [workflow-sources.md](workflow-sources.md) |
