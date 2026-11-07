# Challenge: ci-path-scoping (decision)

- Challenger: gpt-6-sol via GitHub Copilot CLI
- Base commit: `443e3bfbcc3148733e937abca74c0b8994cf3eec`
- Date (UTC): 2026-11-06T18:40:29Z

VERDICT: RETHINK

## 1. Pre-mortem

- [decision] A pull request merges with green gates but without a required scan or suite. `NFR-TEST-6` requires backend, frontend, journeys and ZAP on **every** pull request; `R33` requires every suite and the scans. A successful job whose substantive steps were skipped does not meet those requirements.
- [story] Trivy misses a changed misconfiguration. The proposed `trivy.list` is framed around image inputs and manifests, but `scripts/security-local.sh` runs `trivy fs` over the repository, including Terraform and Compose. A Terraform-only change could skip that scan unless its entire input scope is accounted for.
- [story] A dependency is omitted from a list. The proposed tracked-file ownership test proves that each file appears *somewhere*, not that every job lists every input it reads. Existing cross-area reads include `backend/openapi.json` in frontend generation and `frontend/src/theme/tokens.css` in platform tests.
- [story] The new path script fails open on unusual filenames: the existing script converts NUL-delimited `git diff -z` output to newline-delimited text. A changed path containing a newline can therefore be split before matching. The proposed `!` exclusions add an undecided precedence rule that could also hide an input.

## 2. Simplest design

- [decision] **Under the current PRD and initiative, keep their every-PR checks** and retain the already-approved path scope for `e2e-regression`. To obtain the proposed savings, first amend `NFR-TEST-6`, `NFR-INFRA-2`, `R33` and ad-012 explicitly; an AD-32 and AGENTS.md amendment alone cannot override them.
- [story] After that decision, use the existing diff-and-step-guard pattern for the four CI jobs. Keep Trivy’s repository filesystem/config scan on every pull request; condition only image builds and image scans on their inputs, while retaining full scans on main and the weekly Security run. Do not add a repository-wide “claimed by some list” test as a substitute for tests of each job’s actual dependencies.

## 3. Assumptions

- [story] The quoted savings are estimates from one run, not established outcomes. `#11` demonstrates wasted work, but the stated docs-only and Terraform-only times have not been measured with this design.
- [story] “Main runs everything” appears to include `e2e-regression`, yet its current main behavior is pass-with-notice and the proposal also says it *keeps* that behavior. Specify which is intended; `ci.yml` currently has no schedule, whereas `security.yml` does.
- [story] The drift guard assumes Compose build contexts and mounts capture job dependencies. They do not capture all test and script reads, as the cross-area examples above demonstrate.

## 4. Standards

- [story] A local composite action with outputs and step-level conditions is a documented GitHub Actions mechanism, not an invented one: [composite actions](https://docs.github.com/en/actions/tutorials/create-actions/create-a-composite-action). Keeping jobs present also addresses GitHub’s documented behavior that a skipped required job reports success: [job conditions](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-jobs-with-conditions). The **custom path-list language**, especially `!` precedence, is not a framework standard; either justify and specify it with fail-closed tests, or use documented [GitHub path-pattern semantics](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#onpull_requestpull_request_targetpathspaths-ignore) inside the always-running jobs. Workflow-level path filtering alone would not satisfy the required-check design.

## 5. Conflicts and gaps

- [decision] **PRD `NFR-TEST-6`, `NFR-INFRA-2`; initiative `R33`; ad-012 clauses 1 and 3; architecture `AD-32`:** the proposal changes their every-pull-request test/build/scan guarantees. It acknowledges amendments to ad-012 and AD-32, but not to the PRD or initiative. The human’s Trivy-scoping answer needs an explicit resolution of `R33` and ad-012’s pre-merge image-scan decision.
- [decision] **AD-32 / ad-012 test:** “Every job reports, selected steps run by changed path” is explainable in two sentences and uses documented Actions primitives; it is proportionate to the measured heavy-job cost, but **not necessary** to meet the existing requirements—it changes them. Its PRD/initiative amendments must be decided first.
- [decision] **Trivy amendment test:** conditional *image* scans are explainable and use the same documented mechanism, but a blanket conditional Trivy job is neither proportionate nor compliant while its filesystem scan covers the whole repository. Preserve that scan or explicitly decide and document a narrower security requirement.
- [story] **Drift-guard addition test:** Compose-input assertions are explainable and useful; the every-tracked-file ownership assertion is not a proof of job coverage and is unnecessary. Decide the `!` rule’s precedence, how exclusions affect coverage assertions, and the main `e2e-regression` behavior before making stories.
