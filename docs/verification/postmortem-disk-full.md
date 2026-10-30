# Postmortem: build machine disk full (2026-10-30)

Trigger: free disk under 15 GB (AGENTS.md, Continuous improvement).

## What happened

Around 14:00 the M2's data volume reached 100% (3.6 GiB free). Docker Desktop's VM disk then went read-only: Postgres in the Compose stack and in Testcontainers died ("read-only file system"), 6.2's merge-rebase suite run became invalid (1350 environmental failures) and 5.4's QA stopped part-way. Neither story's code was at fault.

## Why

- 65 git worktrees (28 GB): every builder, rebase and fix round today ran in its own worktree, and merged ones were not removed after each merge as the machine-hygiene rule requires.
- 11 GB of throwaway review trees and a scanner cache in the session scratchpad.
- 21.6 GB of Docker build cache (18 GB reclaimable), from many stack rebuilds.

Category: environment or tooling (supervisor hygiene).

## Done

- Removed 54 merged worktrees and their branches (kept 6.2's rebase and 5.4's tree), removed the scratchpad throwaway trees and the scanner cache: 15 GB free.
- Stopped the Compose stack left up with no lock.

## Needs a human decision

- Restart Docker Desktop to clear the read-only VM disk (the daemon is shared).
- Prune Docker build cache (`docker builder prune`), which is not scoped by project; AGENTS.md allows only DisputeDesk-scoped pruning.

## Countermeasure (proposal)

A deterministic check instead of a reminder: `scripts/lane.sh start` and the supervisor's merge step refuse to start a builder when free disk is under 20 GB and print the merged worktrees that can be removed (`git worktree list` against `git branch --merged main`).
