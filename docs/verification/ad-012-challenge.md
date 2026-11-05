# Challenge: ad-012 (decision)

- Challenger: gpt-6-sol via GitHub Copilot CLI
- Base commit: `085144676105689b96757261c906da637198e8b8`
- Date (UTC): 2026-11-05T08:23:32Z

The proposed flow changes both the CI gate and the repository’s merge guarantees. I’ll compare those changes against the existing workflows, deploy role, and documented requirements before judging whether the design is ready for stories.

VERDICT: RETHINK

## 1. Pre-mortem

- [decision] A vulnerable image reaches `main`: ad-012 removes Security’s pull-request image scan and moves image scanning into `publish` **after merge**. That contradicts PRD NFR-SEC-8 and initiative R33, which require the scans on every pull request and untriaged high or critical findings to block it.
- [story] Deployment remains permanently skipped or becomes callable from an unintended branch. `publish` currently writes digests to the job summary, not job outputs; the proposed reusable call needs actual outputs. Changing OIDC trust from `main` to `environment:dev` also makes the environment’s branch restriction essential.
- [story] The first rollout stalls: Security has no gate job today, auto-merge must be enabled in repository settings, and a ruleset alone does not establish “squash only” while other merge methods remain enabled.

## 2. Simplest design

- [decision] Keep pull requests, up-to-date required checks and squash merging, but **retain image scanning before merge**. Run the existing PR image scan as the required Security check; build, scan and push the release images once on `main`. Those are two builds at different gates, not unnecessary duplication: one prevents an unsafe merge; the other scans the bytes being published.
- [story] Add a Security gate that explicitly checks its jobs’ results, and require it alongside `ci-passed`. Expose the five published digests as job outputs. Use the existing deploy workflow’s steps for both the automatic call and manual rollback, with `environment: dev` on its **called job**, not its calling job.
- [story] Local composite actions are justified for the three identical Compose installs and two Trivy installs. Do not add a path-filter action or merge queue. Arm auto-merge only after the existing reviewer, verifier, security and QA stages pass; then confirm the PR merged and the `main` run completed.

## 3. Assumptions

- [story] **Repository settings can enforce this flow.** The repository is public and user-owned, and squash merging is allowed; the proposed ruleset, disabled alternative merge methods and auto-merge setting have not been verified. GitHub requires [auto-merge to be enabled](https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/automatically-merging-a-pull-request).
- [story] **A disabled deploy should report a notice.** The decision says a deploy job runs only when `DEPLOY_ENABLED` is true *and* says it writes a notice otherwise. A skipped job cannot write that notice; choose one behavior.
- [story] **Stacked stories remain workable.** AGENTS.md and `docs/process/agentic-sdlc.md` build dependent stories on unmerged story branches. The decision does not say how their PR bases are retargeted, checked and squash-merged in order.

## 4. Standards

- [story] A “Security workflow’s gate” is named but not specified or implemented. Use an ordinary [required GitHub status check](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches#require-status-checks-before-merging) backed by an Actions job that fails if a needed security job failed, was cancelled or was skipped unexpectedly; require the [Code scanning results check](https://docs.github.com/en/code-security/how-tos/manage-security-alerts/manage-code-scanning-alerts/triage-alerts-in-pull-requests#managing-severity-levels-for-check-failures) if CodeQL findings themselves must block merges.
- [story] Environment-scoped OIDC is documented, but its subject no longer identifies `main`. Follow [GitHub’s AWS OIDC guidance](https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-aws): restrict `dev` to `main`, retain exact audience and immutable repository identity in the trust policy, and update the Terraform assertion and one-time trust check. GitHub’s [reusable-workflow guidance](https://docs.github.com/en/actions/how-tos/reuse-automations/reuse-workflows) puts `environment` on the called job.
- [story] No invented mechanism is needed for composite actions, reusable workflows or PR checks; each is a documented GitHub Actions feature. The missing pieces are gate wiring and explicit enforcement, not a new abstraction.

## 5. Conflicts, gaps and decision tests

- [decision] **PRD NFR-SEC-8; initiative R33; AD-32:** deleting the PR image scan changes a required pre-merge check without saying so. Either retain it or explicitly change those requirements with a human decision.
- [decision] **AD-28; initiative R38:** automatic deployment and environment-scoped OIDC amend AD-28’s *manual* deployment rule and its “no new AWS-only mechanisms before an account exists” constraint. Record the amendment and its offline limit; Terraform mocks cannot prove the live OIDC assumption.
- [decision] **AGENTS.md Commits and story flow:** squash produces a new `main` commit instead of fast-forwarding the branch commit that CI tested. The human chose this change, but the replacement invariant must say that PR checks test GitHub’s proposed merge tree, while `main` publishes and scans its resulting squash commit. Update the process rule explicitly.
- [story] **Ruleset and rollout gaps:** specify the exact required check names, disable other repository merge methods, enable auto-merge, and migrate the current `story/**` trigger only after the new PR gates are demonstrably required. GitHub’s [linear-history rule](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets#require-linear-history) permits rebase as well as squash; it does not mean “squash only.”

[story] **Decision tests, clauses 1–6:** (1) PR checks and a CI gate are explainable, standard, proportionate and necessary, provided the gate checks every intended result. (2) A protected `main` is explainable, standard and necessary; its stated squash enforcement is incomplete. (3) Publishing scanned images once on `main` is explainable and standard, but removing the PR scan is neither proportionate nor compatible with R33. (4) Reusable deployment is explainable and standard; automatic deployment serves the human’s choice, but broadened OIDC trust without environment protection is not proportionate. (5) Two local composite actions are explainable, standard and proportionate to five repeated install blocks, though no requirement forces that refactor. (6) PR-based supervisor merging is explainable and standard and implements the chosen flow; `--auto` is not necessary to do so and must not bypass the pipeline stages.
