# Decisions

The human's answers to the `[decision]` points of the pre-build reviews (AGENTS.md, Story flow), one row per point.

| Feature | Point | Answer | Date |
|---|---|---|---|
| feature-ids | D1: when the lows batch and the retrospective run. | When `docs/verification/lows.md` holds 10 open rows or five features have merged since the last batch, whichever comes first, as one `feature/lows-<yyyy-mm-dd>` pull request with the retrospective beside it. | 2026-11-06 |
| ci-e2e-regression | D1: which changed paths start the regression suite. | Every input of the stack: every Compose build context and bind-mount source, plus the job's own files (`.github/workflows/ci.yml`, `.github/actions/install-compose/`, `scripts/regression-paths.sh`). | 2026-11-06 |
| ci-e2e-regression | D2: AD-32 runs every check job on every pull request with no docs-only skip, and the new job skips its suite by path. | Amend AD-32 to name `e2e-regression` as the one path-scoped suite. | 2026-11-06 |
| guard-allow-list | D1: programs that run git inside themselves (make, npm scripts, `dotnet` targets, repository scripts, the Copilot child process) stay unread by a per-call hook. | Name the limit in the guard's header, beside the cross-call limit and with the same upgrade path, and require the human as a reviewer on the `dev` environment before `DEPLOY_ENABLED` is turned on. | 2026-11-06 |
| guard-allow-list | D2: CLAUDE.md says the guard asks only for actions on a deployed environment, while the guard also asks the main session on commands it cannot read. | Amend CLAUDE.md so a command the guard cannot read is a main-session ask. | 2026-11-06 |
| reviewer-docs | The accepted risks in `docs/production-path.md` whose reasons said the AWS environment is never applied (P64 to P71, P80). | Still accepted for the AWS demo: it holds only synthetic data on a dedicated account. The reasons now read "not yet deployed" and are reworded again after the first deploy. | 2026-11-06 |
| gateway-musl | CVE-2026-78661 (HIGH, x/net/html denial of service) in Zitadel's own binary, with no fixed Zitadel release. | Triage it like go-jose: a dated `.github/trivyignore.yaml` entry scoped to `app/zitadel`, expiring 2027-02-05, and production-path P78 extended; upgrade Zitadel and drop the entry once a release ships x/net 0.62.0. | 2026-11-07 |
| ui-refresh | The UX handoff and mockups are uncommitted in the main checkout, out of reach of builder worktrees. | Land `docs/ux/**` first in a docs-only pull request, with the six feature specs. | 2026-11-08 |
| ui-detail-start | HANDOFF 3.10 takes "Disputed on" from the first timeline entry, whose order is a mapping convention only. | Use the detail response's `createdAt`, which the contract guarantees; HANDOFF, DESIGN and EXPERIENCE say so. | 2026-11-08 |
| ui-linking | HANDOFF 3.9 styles "the active OTP cell", but the code is one plain input. | Build six cells with shadcn/ui Input OTP in the project's `new-york` style, styled to the handoff; autofill, paste, validation and submit unchanged. | 2026-11-08 |
| ui-notices-states | The handoff names shadcn `Alert`, which the project lacks; only StepProgress and StateSurface are new components. | Add shadcn's `alert.tsx` and rebuild ProblemNotice and ErrorSummary on it; callers unchanged. | 2026-11-08 |
