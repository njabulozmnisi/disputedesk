# reviewer-docs review

Small-tier, docs-only change on `feature/reviewer-docs`: the uncommitted diff in the main checkout against HEAD
`0e8317c` (= origin/main). `backend/README.md` and `backend/fixtures/README.md` were out of scope. Review stopped early
at the coordinator's turn limit; the checks not done are listed under "Not checked".

## How it was checked

- **Mermaid:** every block in the changed files was rendered with mermaid-cli 11.17.2 (`mmdc -i <file>.md`), and each
  was compared with the code: `docker-compose.yml`, `infra/deploy/aws/compose.yml`, `infra/deploy/aws/deploy.sh`, the
  Terraform modules (network, compute, database, deploy-role), the `.csproj` references, `Dispute.IsAllowed`,
  `frontend/src/app/router.tsx` and the EF model snapshot.
- **Seed values:** the logins, IDs, accounts and transactions (TXN-A-0011 is the SLA demo, due 5 minutes after start;
  TXN-A-0026 is Submitted) were checked against `backend/fixtures/local.json` and `SeedDispute.cs`.
- **"How it was built":** checked against `.claude/agents/*.md` (models), `scripts/verify-with-copilot.sh`,
  `docs/process/workflow-sources.md`, `scripts/floor-guard.sh`, `.githooks/` and `scripts/bootstrap.sh`.
- **Versions and CI job list:** checked against `frontend/package.json`, `envs/dev/terraform.tf`, `ci.yml` and both
  `global.json` files.
- **Conventions:** checked against the policies in `OperationsAuthentication.cs`, `docs/problems/` and `src/test/`.
- **Links:** every relative link and anchor in the changed files resolves.
- **terraform README:** steps 1–8, the heading *A half-done database init*, post-deploy checks 1–11, and the citations
  in `deploy.sh`, `deploy.yml`, `ci.yml` and `deploy-role/main.tf` are all intact.

The rest matches the code: the layer diagram, the local component view, the infra README's local stack diagram, the
deploy sequence diagram, the lifecycle state diagram, the frontend pages and API-client diagrams, the ER diagram's
columns, types and foreign keys, the agent models, the CI job list and the versions.

## Findings

| file:line | finding | severity | fix |
|---|---|---|---|
| README.md:59 (and the removed "Seed identities" note) | NFR-SEC-11 (`docs/prd/prd.md:594`) requires: "The README notes that a real deployment sends OTPs by SMS" (story 7.14 criterion 7). The rewrite removed that note: Mailpit stands in for SMS, a real deployment sends the OTP by SMS to the cellphone on the bank's record, and the AWS Mailpit UI is behind basic auth. No other README says it now. This is a lost requirement. | medium | Add one sentence after step 5, or in the Run table's Mailpit row: Mailpit stands in for SMS here (`ISmsSender`), a real deployment sends the OTP by SMS to the number on the bank's record, and in AWS Mailpit's UI is behind basic auth ([post-deploy check 3](infra/terraform/README.md#post-deploy-checks)). |
| docs/architecture/architecture.md:587 | The AWS deployment view adds `API --> RP`, but the api never connects to Redpanda in AWS. `infra/deploy/aws/compose.yml` overrides the api's environment with no `Kafka__BootstrapServers` and no `Worker__InProcess`, and `Program.cs:65-69` registers the relay (the only Kafka producer) only when `Worker:InProcess` is true. Only the worker reaches Redpanda in AWS. | medium | Delete `API --> RP` from the AWS view and keep `WK --> RP`. |
| infra/README.md:68 | The same error in the "Deployed on AWS" diagram: `API --> RP[[redpanda]]`. | medium | Declare the node as `RP[[redpanda]]` on the `WK --> RP` line and drop the api edge. |
| docs/production-path.md:137-139, 241, 273, 277, 281, 285, 289, 293, 297, 301, 333, 337 | P46's heading ("AWS dev environment, never applied") and body ("has never been applied, because the account does not exist"), plus about ten *Why it waits* lines ("the AWS environment is never applied in this project (P46)"), now contradict the new status in `infra/terraform/README.md:3-7` and `infra/README.md:53-55`. The account exists and step 4's first apply created the network, instance and database. P46 also still says the RDS master password is held in state, which `infra/terraform/README.md:49-51` says it is not (write-only arguments). | medium | Update P46: the environment is partly applied (network, instance and database), the application has never been deployed, and state holds the origin-verify value and the budget address. Then reword each "never applied" reason to "the application has never been deployed". Several of those reasons cite the human decision of 2026-10-31, so show the rewording to the human. |
| docs/process/agentic-sdlc.md:80 | Broken diagram, which predates this diff but is in a file the diff touches. In the story-flow `sequenceDiagram`, the message `S->>B: <id>-fixes.md to a fresh builder; the stages re-run on the delta` contains `;`, which Mermaid reads as a statement separator. mermaid-cli fails with a parse error ("got 'NEWLINE'"), so GitHub will not render the diagram. | medium | Replace the `;` with a comma or `#59;`, for example `S->>B: <id>-fixes.md to a fresh builder, then the stages re-run on the delta`. |
| infra/README.md:38 | The old README ran the smoke check under the stack lock (`scripts/test-lock.sh run stack <owner> -- infra/tests/smoke.sh`) and warned that it wipes the shared `disputedesk` stack, whichever worktree started it. The new line keeps "starts and ends with down -v" but drops the lock and the warning. AGENTS.md (Dispatch rules) keeps stack use behind `scripts/test-lock.sh`. | low | Restore the `scripts/test-lock.sh run stack <owner> -- …` form, or add "run it only under the `stack` lock". |
| infra/README.md:135 | Same loss for the platform tests: the gateway and database-init tests start containers on the shared Docker daemon, and the old README ran them under the `stack` lock. | low | Add "Run them under the `stack` lock (`scripts/test-lock.sh run stack <owner> -- bash -c '…'`)." |
| infra/README.md:100 | "Deploys run from `main` only, and stay switched off until `DEPLOY_ENABLED` is `true`." Only the continuous deploy (CI's `deploy` job) waits on `DEPLOY_ENABLED`. A hand-dispatched `deploy.yml` (the first deploy, rollback) runs without it, as `terraform/README.md` step 8 relies on. | low | "Continuous deploy stays switched off until …; a hand-dispatched `deploy.yml` runs regardless (first deploy, rollback)." |
| infra/README.md:182 | "no output means it matches" drops the old caveat that a difference can be a parameter GitHub adds (`allowed_merge_methods`, `do_not_enforce_on_create`). An owner who sees such a difference may wrongly re-apply the ruleset. | low | Add "a difference can be a parameter GitHub adds; record it with the read-back." |
| README.md:55 | "withdraw a Submitted one" can be read as withdrawing A's seeded Submitted dispute on TXN-A-0026, which step 6 needs in `submitted`. The old README also warned that the e2e tests change the seeded disputes, so reset first. Both points are gone. | low | "withdraw the one you just raised"; add to line 42 that the e2e tests change the seeded disputes, so reset before the walkthrough. |
| README.md:42 | Lost: the seed's dates are relative to start-up, so after a day boundary the data drifts (90-day window, SLA demo) and a reset is needed. | low | Append "The seed's dates are relative to start-up; reset after a day boundary." |
| README.md:53 | Lost note (story 7.7 criterion 1): an AWS deployment uses the same fixture logins and published passwords. It survives only in `infra/terraform/README.md:43`. | low | Append "An AWS deployment uses the same logins ([infra/terraform/](infra/terraform/README.md#know-before-you-apply))." |
| infra/terraform/README.md:4-6 | "Steps 1–3 are done" sits beside "CloudFront … wait on account verification (step 1.4)". Step 1.4 is that verification, so step 1 is not fully done. | low | "Steps 1–3 are done except 1.4: the CloudFront verification request is still open." |
| docs/architecture/architecture.md:742 | `Disputes \|\|..o{ AuditLogEntries` says every audit entry has exactly one dispute, but `AuditLogEntries.DisputeId` is nullable ("linking entries have none"). | low | Use `Disputes \|o..o{ AuditLogEntries : "DisputeId, no FK"`. |

## Not checked

- `docs/onboarding.md` and `frontend/README.md`: commands were not run. The npm script names and the `scripts/bootstrap.sh` behaviour were read and match.
- `infra/terraform/README.md`: the commands in steps 2–8 and in the post-deploy checks were not run (AWS commands are gated). The status lines were not checked against the AWS account.
- `infra/README.md`: the PlatformTests class table (for example, whether `LabelPolicyTests` exists) was not checked.
- `docs/README.md`:
  - the "What enforces the rules" guard-hook row was not checked against `.claude/hooks/guard.py` in detail;
  - the `research/` folder list was not checked against `docs/research/`.
- The stack was not started, so no URL, port or login was tried live. They were checked only against `docker-compose.yml` and `local.json`.
- Forbidden terms and secret scan: not run (`scripts/secret-scan.sh`, `.claude/forbidden-terms.local`).
- Neutral voice and glossary terms: the files were read but not swept systematically against `docs/glossary.md`.
- Unrelated to this docs diff, raised for the supervisor: `Program.cs:75` runs migrations only when `Worker:InProcess` is true, but `infra/deploy/aws/compose.yml` says "the Api applies the migrations" in AWS, where `InProcess` is unset. Not investigated.
