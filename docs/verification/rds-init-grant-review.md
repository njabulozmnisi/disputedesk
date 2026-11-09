# rds-init-grant review

Reviewer (post-build), PR #17, branch `feature/rds-init-grant`, commit `eb2e8bf`, diff `git diff origin/main...eb2e8bf` (7 files). Small-tier fix to the database init on RDS. Read-only: one correctness pass, a simplicity and standards pass, and the security checklist (`.claude/skills/security-story/SKILL.md`).

Read: the diff; `infra/images/postgres/10-roles-and-databases.sh`; the deploy script's count query, state `case` and init call (lines 118-154); `DatabaseInitFixture.cs`, `DatabaseInitTests.cs`, `DeployScriptTests.cs` (the first-deploy environment test), `DeployCopy.InitCall`; `docker-compose.yml` (postgres service); `infra/terraform/README.md` "A half-done database init" and post-deploy check 6; `docs/verification/rds-init-grant-build.json`; `git grep` for `createrole_self_grant`, `PGOPTIONS` and the init script's callers at `eb2e8bf`.

Run: `gitleaks git --log-opts=origin/main..eb2e8bf` (1 commit, no leaks); `gh pr checks 17` (backend, platform, e2e, floor-guard, terraform, frontend, CodeQL, gitleaks, trivy, vulnerable-packages and security-passed pass; e2e-regression pending at read time). Not run: the PlatformTests suite. The builder's results file (`TestResults/...15_19_43.567573.trx`) was in worktree `agent-a8073d506f8eedb16`, which no longer exists, so I could not read it. The verifier owns the results file and the breaks. CI's `platform` job passing on this head is the run I saw.

## Answers to the brief

**1. All three callers, and re-runs.**

- **Local Compose.** `POSTGRES_USER: postgres` (`docker-compose.yml:36`) runs the script from `docker-entrypoint-initdb.d` as a superuser. A superuser can grant any role, so `GRANT disputedesk TO CURRENT_USER WITH SET TRUE, INHERIT TRUE` succeeds and changes nothing a superuser needs. The builder's smoke run shows two `GRANT ROLE` lines and the expected ACLs. I did not repeat it.
- **DatabaseInitTests' master.** Plain PostgreSQL 18.6 gives a non-superuser CREATEROLE creator an implicit ADMIN-only membership, granted by the bootstrap superuser. That ADMIN option lets `dbmaster` grant itself SET and INHERIT. The new assertion filters on `set_option`, so it reads the init's own row and not the implicit one. Break 1 in the build record (remove the GRANTs) fails with the RDS error; break 2 (INHERIT FALSE) fails on the REVOKE, the ALTER DEFAULT PRIVILEGES and the WARNING check.
- **RDS.** RDS creates no membership row at all, so `createrole_self_grant` had nothing to act on. The fix does not depend on that setting. Only a probe shows that the GRANT itself succeeds on RDS for a master with no ADMIN row (build record, assumptions). Nothing local can show it; the next deploy plus post-deploy check 6 is the proof.
- **Re-runs.** The init still runs only on `"0 0"`. `"3 2"` skips it, and any other count fails closed. The GRANT sits between `CREATE ROLE` and `CREATE DATABASE`, outside a transaction (CREATE DATABASE cannot run inside one). If the GRANT fails, the count reads `1 0` and the run lands in the fail-closed branch, with the same shape and recovery as before. `DatabaseInitTests.FirstThenLaterDeploy_...` proves first run, then a later run that skips. Re-run safe.

**2. Least privilege.** The end state is the same as the one story 7.5 approved: `createrole_self_grant=set,inherit` produced the same SET and INHERIT membership. So the privilege is unchanged from the reviewed design; only the way of getting there changed. The smaller-looking alternative keeps INHERIT FALSE and wraps the owner statements in `SET ROLE <owner>` … `RESET ROLE`. It grants no less in practice:

- it still needs SET TRUE, and any `dbmaster` session can then `SET ROLE disputedesk` and act as the owner ([GRANT, role membership options](https://www.postgresql.org/docs/18/sql-grant.html); [role membership](https://www.postgresql.org/docs/18/role-membership.html));
- revoking the grant afterwards adds no boundary. On plain PostgreSQL the master keeps ADMIN and can grant itself again ([role attributes, CREATEROLE](https://www.postgresql.org/docs/18/role-attributes.html)), and on RDS the same GRANT succeeds without any membership;
- it costs more statements and more ways to fail. `REVOKE ALL ON DATABASE … FROM PUBLIC` run as the owner really removes PUBLIC's CONNECT, so the later `\connect disputedesk` as a non-inheriting `dbmaster` would fail unless the script is reordered. `ALTER DEFAULT PRIVILEGES` would have to run under SET ROLE ([ALTER DEFAULT PRIVILEGES](https://www.postgresql.org/docs/18/sql-alterdefaultprivileges.html): only for roles you are a member of). The README's `DROP DATABASE`, which only the owner may run ([DROP DATABASE](https://www.postgresql.org/docs/18/sql-dropdatabase.html)), would need SET ROLE too.

`dbmaster` is used only by the deploy and by break-glass access. The app and Zitadel connect as their own roles, and the app role owning its database is already deferred as E-F2 (P27, P28). The form is acceptable and the SET ROLE variant is not worth it. The need for SET comes from [CREATE DATABASE](https://www.postgresql.org/docs/18/sql-createdatabase.html) ("you must be able to SET ROLE to that role"). The need for INHERIT comes from [GRANT](https://www.postgresql.org/docs/18/sql-grant.html) (a member of the owning role grants and revokes "as though it were issued by the containing role", which needs the owner's privileges through inheritance). I did not re-fetch these pages in this session; the quotes match the PostgreSQL 16+ docs as I know them.

**3. Do the tests pin RDS behaviour?** Partly. The tests catch the regression in its old form: putting PGOPTIONS back into the deploy script fails `FirstDeploy_PassesEveryDatabaseValueToTheContainersByNameOnly` on both the argv (`InitCall`) and the environment (`PGOPTIONS = ""`). They also catch the GRANTs being removed. They cannot reproduce what actually broke RDS: the fixture's master gets an ADMIN membership, so `createrole_self_grant` works there. A change that drops the GRANTs and sets `createrole_self_grant` another way passes every test and breaks RDS again. Two examples: a `SET createrole_self_grant = 'set, inherit'` in the init's SQL, or PGOPTIONS back in with `InitCall` edited to match. See L1.

**4. Comments and docs.**

- The deploy script comment (137-138) is accurate. It names SET for CREATE DATABASE; the init's own comment explains INHERIT.
- The init comment (15-17) is accurate.
- The fixture comments are accurate: the `MasterShape` summary, and the new note at 126-127 that PostgreSQL 16+ gives ADMIN only and RDS gives no membership.
- The comment in `DeployScriptTests` (93-94) matches the test.
- Post-deploy check 6 still holds: it checks only the database owners, and the change affects neither the owners nor its commands.
- "A half-done database init" holds for a half-done run under the new script: `dbmaster` inherits the owner, so `DROP DATABASE` works. It does not address the half-done state the failed deploy most likely left on RDS, a role `disputedesk` with no master membership. See L2.
- The done stories 7.5 and 7.17 still describe `createrole_self_grant`. They are records of done work and keep their text (AGENTS.md, Ticket numbering). Not a finding.

## Pass 1: correctness

The fix is correct for all three callers. The build record maps each criterion to a test that failed before the change. No timestamps, endpoints or Result mapping are involved.

## Pass 2: simplicity and standards

Two documented GRANT statements replace a session setting that RDS ignores, and the PGOPTIONS plumbing is gone. This is smaller than before, and each statement can be explained in one sentence. Mechanisms: the GRANT role options and the CREATE DATABASE owner rule, both PostgreSQL 18 documented (links above). Nothing is home-grown. No speculative options.

## Security checklist (below High-stakes)

- AuthN/Z, browser session, linking, headers: not touched.
- Secrets: passwords still reach psql through `\getenv` and are bound as psql variables. Docker gets them as `-e NAME` only. The GRANT carries no secret. `DatabaseInitTests.AssertNoSecret` still covers every captured output, and gitleaks finds no leak in the commit.
- Data: the role ACL outcomes are unchanged (CONNECT, USAGE on `public`, `qa_readonly`'s default privileges and read-only setting are all asserted). The master's standing membership is the same as the approved 7.5 design (answer 2).
- Supply chain: no package, image or Terraform change. Only shell scripts and tests changed, so `scripts/security-local.sh` does not apply. CI's trivy and vulnerable-packages checks pass.
- Standards: the database privileges follow the PostgreSQL docs, which fits the least-privilege intent of ASVS 5.0 V13.2.2. That requirement stays partly unmet by design (E-F2, already deferred).

## Findings

| ID | Severity | File and line | Finding | Fix |
|---|---|---|---|---|
| L1 | low | `infra/tests/DisputeDesk.PlatformTests/DatabaseInitFixture.cs:126-127`, `DatabaseInitTests.cs:95-100` | The fixture's plain PostgreSQL master holds an implicit ADMIN membership, so `createrole_self_grant` works there. A change that removes the GRANTs and brings `createrole_self_grant` back another way (a `SET` in the init's SQL, or PGOPTIONS plus a matching `InitCall` edit) passes every test and repeats the RDS failure. The tests prove the fix but do not pin the RDS behaviour that caused the defect. | Add one source assertion that neither `infra/images/postgres/10-roles-and-databases.sh` nor the deploy script contains `createrole_self_grant`. Record it as the capture's escape countermeasure (environment: the fixture cannot model RDS role management). The real RDS proof stays post-deploy check 6. |
| L2 | low | `infra/terraform/README.md:263-292` | The failed deploy most likely left role `disputedesk` on RDS, created by the old script, with no master membership. The next deploy will stop in the fail-closed branch and send the operator to this recovery. On PostgreSQL 16+, `DROP ROLE` by a non-superuser needs ADMIN OPTION on the role ([DROP ROLE](https://www.postgresql.org/docs/18/sql-droprole.html)). Whether RDS's `rds_superuser` master may drop it without a membership is unverified (the builder's open question). | When running the recovery, if `DROP ROLE disputedesk` is refused, run `GRANT disputedesk TO CURRENT_USER;` first, then add that line to the README in the records or lows batch. Do not add it before RDS shows it is needed. |

No high or medium findings. The lows go to `docs/verification/lows.md` at merge.

```json
{"status": "approve", "record": "docs/verification/rds-init-grant-review.md", "findings": [{"severity": "low", "file": "infra/tests/DisputeDesk.PlatformTests/DatabaseInitFixture.cs", "line": 126, "issue": "The plain PostgreSQL fixture master holds an implicit ADMIN membership, so createrole_self_grant works there; removing the GRANTs and bringing createrole_self_grant back another way passes every test and repeats the RDS failure.", "fix": "Add a source assertion that neither the init script nor the deploy script contains createrole_self_grant; record it as the escape countermeasure."}, {"severity": "low", "file": "infra/terraform/README.md", "line": 263, "issue": "The recovery's DROP ROLE disputedesk may be refused on RDS for the role the failed deploy left, which the master has no membership in (PostgreSQL 16+ DROP ROLE needs ADMIN OPTION); unverified.", "fix": "If RDS refuses it, run GRANT disputedesk TO CURRENT_USER first, then add that line to the README in the records or lows batch."}], "assumptions": ["The GRANT succeeding on RDS for a master with no membership rests on the probe cited in the build record; it cannot be reproduced locally.", "The PostgreSQL 18 doc statements cited were not re-fetched in this session.", "The builder's results file could not be read (its worktree is gone); CI's platform job passing on eb2e8bf was the run I saw."], "open_questions": [], "conflicts": []}
```
