# aws-admin-lockdown: post-build review

- Date: 2026-11-10
- Worktree: `.claude/worktrees/agent-a00452c8607be46db`, uncommitted diff against `main` (566a89f), plus the supervisor's two docs edits in `docs/production-path.md` (P50, P80)
- Builder report: `docs/verification/aws-admin-lockdown-build.json` (448/448 PlatformTests passed; 7 breaks failed as expected per `TestResults/breaks-run.log`, read, not re-run)
- Intent: the human decision of 2026-11-10. Mailpit's inbox in AWS is about to become public, so no human Zitadel administrator may stay able to sign in there.
- Result: **approve**. No high or medium findings. Two lows.

## Pass 1: correctness

**Zitadel v4.20.0 API calls.** I checked these against the tagged source, `raw.githubusercontent.com/zitadel/zitadel/v4.20.0/...`.

- `InternalPermissionService/ListAdministrators` (`proto/zitadel/internal_permission/v2/internal_permission_service.proto`, `query.proto`). The response is `administrators[]`, and each entry has `user.id` (`message User { string id = 1; }`). There is one entry per resource (instance, organization, project, project grant), so the `| unique` in `ADMIN_IDS` is correct, and its test is the right one. With no filter it lists every administrator the caller may read. The provisioner is IAM_OWNER, so it reads them all.
- `UserService/ListUsers` with `inUserIdsQuery.userIds` (`proto/zitadel/user/v2/query.proto`, `in_user_ids_query = 10`; the JSON name is `inUserIdsQuery`). The result is `result[]`, each with `userId`, `state` and the `human` or `machine` oneof. `select(.human and .state != "USER_STATE_INACTIVE")` therefore never selects a machine user. The provisioner (machine, IAM_OWNER) and `operations-tools` (machine, no admin role) are never touched, and the PAT is never read beyond its existing use.
- `UserService/DeactivateUser` (`internal/command/user_v2.go`, `DeactivateUserV2`). It refuses INITIAL (`CantDeactivateInitial`) and INACTIVE (`AlreadyInactive`) users and accepts ACTIVE and LOCKED ones. Skipping INACTIVE makes the step idempotent, and the test covers LOCKED. An INITIAL human administrator fails the hook, so the deploy fails closed, as the build report states.
- A correction to the build report's assumptions (no code change needed). An empty `inUserIdsQuery` does not become `(1=0)`. `NewUserInUserIdsSearchQuery` calls `NewInTextQuery`, which returns `ErrEmptyValues` for an empty list (`internal/query/user.go:771`, `search_query.go:314`). The call errors, `call` returns 1, and the hook fails closed. The case is also unreachable, because the provisioner is always an administrator.

**Pagination.** Both lists read only the first page. `DefaultQueryLimit: 100` (`cmd/defaults.yaml:1044`) confirms the shortcut comment's number. Once ListAdministrators returns at most 100 entries, there are at most 100 unique IDs, so ListUsers on those IDs always fits in its first page. The weak point is ListAdministrators itself; see L1.

**Failure handling.** Under `set -euo pipefail`, a failure in `admins=$(rpc … | jq …)` stops the script. A failed `DeactivateUser` inside the `while` subshell exits that subshell, and pipefail carries the failure to the main shell. The fixture-login loop already uses the same pattern. The marker is never written, the container never turns healthy, and the api and the gateway never start.

**Fails closed on a bad value.** `case` refuses anything but `true` or `false` before the readiness wait and before any curl call, and the marker is already removed. Unset or empty values stop at `:?`. The test with `True` proves this, including that curl is never called.

**Ordering claim.** The healthcheck requires `/provision/state/provisioned`, which is written only after the step (`provision.sh:294`). The AWS gateway is `depends_on: zitadel: service_healthy` (`infra/deploy/aws/compose.yml:227-228`), and Zitadel publishes no port in AWS. On a fresh instance, `admin` is therefore inactive before Zitadel reports healthy and before the gateway starts, so the claim holds. On the existing instance, a forced recreate may briefly leave the old gateway forwarding to the new Zitadel. That does not matter: deactivation persists in RDS, Mailpit is recreated empty, and check 12 holds Mailpit's publication until after the first such deploy.

**Does deactivation stop the attack?** Yes. The smoke run showed the hosted login never reaches the password step (so the "forgot password" link is never offered) and `CreateSession` returns `Errors.User.NotActive`. A reset mail, even one sent anyway, therefore leads to no sign-in.

**No regressions found.** Nothing in AWS signs in as `admin`. Every place that runs `provision.sh` sets the new required variable: `docker-compose.yml`, the AWS override, and the test environment helper. `verify.sh` does not read it.

**Tests.**
- `Zitadel_HumanAdministrators_AreDeactivatedInAwsOnly` covers both renders.
- `ProvisionScript_DeactivateHumanAdminsNotABoolean_…` covers a refused value on the false side of the bound.
- The two filter tests run the script's own jq filters on realistic answers: duplicate memberships, machine, active, locked and inactive users, and an empty answer.
- The step-text test asserts the guard and the single `DeactivateUser` call. It is a text test, but the class summary explains why the stubbed run cannot reach this step. The real run is the builder's smoke on the pinned image: before and after state, a second run that changes nothing, then reactivation.
- Every test would fail without the change (breaks 1 to 7).

The rest of the Pass 1 checklist (Location headers, `Result` mapping, ownership filter, stored events, `!`) does not apply: there is no backend code in this diff.

## Pass 2: simplicity and standards

- **Is a boolean setting the simplest design?** Yes. It follows the existing `PROVISION_AUTH_DEVELOPMENT_MODE` pattern, with the same validation, in the same file. The alternatives are worse:
  - Running the step unconditionally breaks the documented local console admin.
  - Keying it off `AUTH_DEVELOPMENT_MODE` couples two unrelated meanings.
  - An AWS-only script adds a file.
  - The login policy's `hidePasswordReset` hides reset for every customer, and DefaultInstance applies only to a new instance.
  - Removing the FirstInstance human is not possible on v4.20.0, because setup always creates it (`cmd/setup/03.go`, as cited in the build report).
  - Deleting the user is irreversible, while DeactivateUser can be undone with ReactivateUser.
- The step is about fifteen plain lines. The two named jq filters read well and let the tests run them. No wrapper, option or abstraction has been added.
- **Standards.** The step uses Zitadel's documented UserService v2 `DeactivateUser` and InternalPermissionService v2 `ListAdministrators`, plus Compose lifecycle hooks (`docs.docker.com/compose/how-tos/lifecycle/`). It meets OWASP ASVS 5.0 V6.3.2: default and bootstrap accounts are disabled (`github.com/OWASP/ASVS/blob/v5.0.0/5.0/en/0x15-V6-Authentication.md`). No home-grown mechanism.

## Security checklist (reviewer pass; the security agent owns the full review if this runs as High-stakes)

- **Secrets.** The PAT still reaches curl only through the header file. The new code logs Zitadel user IDs only, with no PII, password or token. `scripts/secret-scan.sh` passed (build report).
- **Supply chain.** No new package or image. Trivy fs and config passed (`scripts/security-local.sh`, build report).
- **Headers, sessions, linking, data.** Unchanged.
- **Observation, outside this diff.**
  - Zitadel's default `DisallowPublicOrgRegistration` is unset, which means false (`cmd/defaults.yaml:1495-1497`).
  - The AWS gateway blocks only `/ui/console` on the identity host (`infra/images/gateway/templates-aws/nginx.conf.template:213-218`).
  - So anyone can open `/ui/login/register/org` and become ORG_OWNER of a new organization of their own.
  - Such users are human administrators. This step deactivates them at the next deploy, and between deploys they control only their own organization.
  - This step makes it no worse. It does feed L1.

## Docs

- **`infra/terraform/README.md`.**
  - The new "Know before you apply" bullet is accurate. The zitadel image already carries curl, and the PAT path matches the provisioner's.
  - Check 12 is a useful gate before publishing Mailpit, but it is weak; see L2.
- **`infra/README.md:109`.** Accurate.
- **`docs/production-path.md` P50.** Accurate, and the 7.10 PRR lifecycle still waits.
- **`docs/production-path.md` P80.** Accurate. In AWS, with every human administrator inactive, a lost PAT can be recovered only with a fresh Zitadel database. As before that change, a fresh database drops self-registered identities.
- **Pre-existing, not this diff.** `infra/README.md:146` gives the local console login as `admin`. The builder observed that v4.20.0's hosted login accepts only `admin@disputedesk.auth.localhost`.

## Findings

| # | Severity | File:line | Finding | Fix |
|---|---|---|---|---|
| L1 | low | `infra/images/zitadel/provision.sh:248-258` | **The first-page shortcut fails open silently, and on the account that matters most.** ListAdministrators sorts by creation date, newest first (`filter/v2/filter.proto`: "The default is descending"). Beyond 100 memberships, the oldest drop off the page first, and the oldest are the FirstInstance `admin`'s. Anonymous org registration can raise that count (see the observation above). The step would then skip `admin`, still log "no human administrator can sign in" and pass the deploy. Nobody can observe the trigger the shortcut names ("when the instance has more admins"). The real risk is small: there are 4 memberships today, and once deactivated, `admin` stays inactive. | Turn the silent miss into a failure. Exit 1 when ListAdministrators' `.pagination.totalResult` is greater than the number of entries returned. That way the log line always holds. Alternatively, send `{"pagination":{"asc":true}}` so the oldest memberships are always on page one. |
| L2 | low | `infra/terraform/README.md:368-370` | **Check 12 can pass without proving anything.** According to the builder's smoke run, an inactive user and an unknown login name both leave the hosted login without asking for a password. A typo, or a wrong suffix, would pass the check while `admin` is still active. The suffix rule (`admin@disputedesk.<AUTH_HOST>`) was observed locally only, not on AWS. This check is the gate before Mailpit is published. | Prove the state through the API with the access path the new bullet already documents. From the zitadel container, with the PAT, call `ListUsers` with `userNameQuery` `admin` and expect `USER_STATE_INACTIVE`. Or check the provisioner log for "human administrator <id> deactivated" on the first deploy, then the API state on later ones. |

## Status

```json
{"status": "approve", "record": "docs/verification/aws-admin-lockdown-review.md", "findings": [{"severity": "low", "file": "infra/images/zitadel/provision.sh", "line": 252, "issue": "First-page shortcut fails open silently: ListAdministrators sorts newest first by default, so past 100 memberships (anonymous org registration can add them) the FirstInstance admin's drop off first, the step skips it and still logs 'no human administrator can sign in'", "fix": "exit 1 when .pagination.totalResult exceeds the entries returned (or request pagination asc:true)"}, {"severity": "low", "file": "infra/terraform/README.md", "line": 368, "issue": "Post-deploy check 12 cannot tell an inactive user from an unknown login name (both skip the password step), and the AWS login-name suffix is unverified, so the gate before publishing Mailpit can pass vacuously", "fix": "check the state through the API from the zitadel container with the PAT (ListUsers userNameQuery admin -> USER_STATE_INACTIVE)"}], "assumptions": ["Compose waits for post_start before starting dependents and the AWS zitadel service publishes no port (read from the compose files, not run)", "Live AWS membership count and admin state are unknown; check 12 is the guard"], "open_questions": [], "conflicts": []}
```

## Fix round 1 (delta review, 2026-11-10)

- Worktree: `.claude/worktrees/agent-a9df08ad4b7d9d358`. I reviewed it against round 0 (`agent-a00452c8607be46db`) with `diff -u` on every changed file. The two worktrees differ only in `docker-compose.yml`, `infra/deploy/aws/compose.yml`, `infra/images/zitadel/provision.sh`, `infra/README.md`, `infra/terraform/README.md`, `ComposeRenderTests.cs`, `PlatformScriptTests.cs` and the build report. `docs/production-path.md` is unchanged between them.
- Fix list: `docs/verification/aws-admin-lockdown-fixes.md` (H1, M1, check 12).
- Builder report: `docs/verification/aws-admin-lockdown-build.json`. I read the results rather than re-running them:
  - `TestResults/platform-run.log` and the trx show 457 of 457 PlatformTests passed, including all 10 new test cases.
  - `TestResults/breaks-run.log` shows all 13 breaks failed as expected (`run-breaks exit=0`).
  - The results files are newer than every changed file.
- Result: **approve**. No high or medium findings. One new low (L3). Round 0's L1 and L2 are closed.

### Each fix item, checked against Zitadel v4.20.0

I read the tagged source at `raw.githubusercontent.com/zitadel/zitadel/v4.20.0/...`.

**H1: existing instance.** `provision.sh:254` calls `call PUT /admin/v1/restrictions '{"disallowPublicOrgRegistration":true}'`.
- `admin.proto:4428-4436` maps `rpc SetRestrictions` to `put: "/restrictions"` with `body: "*"` and permission `iam.restrictions.write`, which IAM_OWNER holds. The script already uses the `/admin/v1` prefix (`/admin/v1/policies/label`).
- `SetRestrictionsRequest` (`admin.proto:9601`) has `optional bool disallow_public_org_registration = 1`, so the JSON name `disallowPublicOrgRegistration` is right.
- It is idempotent, as the comment says. `internal/command/restrictions.go:89-92` returns no command when `wm.NewChanges` is empty, and `SetInstanceRestrictions` then returns the details with no push and no error. The smoke confirms this: the sequence stayed at 1 on the second run.
- It runs unconditionally, locally and in AWS, as the fix list's default asks. Nothing in the repo uses `/ui/login/register/org` (I ran `rg`).

**H1: fresh instance.** The key `ZITADEL_DEFAULTINSTANCE_RESTRICTIONS_DISALLOWPUBLICORGREGISTRATION` is right for v4.20.0 (`cmd/defaults.yaml:1494-1497`, under `DefaultInstance.Restrictions`).
- It is set in `docker-compose.yml` and in the AWS override.
- `Zitadel_PublicOrganizationRegistration_IsClosedOnANewInstance` asserts it in both renders, and the env-key allow-list includes it.
- The smoke ran a throwaway fresh instance: register/org answered 404 and register answered 200.

**Customer self-registration is unaffected.**
- `register_org_handler.go:40-68` reads `restrictions.DisallowPublicOrgRegistration` and answers 404 on GET and 409 on POST.
- `register_handler.go` (the `/ui/login/register` user registration) does not read the restrictions at all (grep: no match).
- The smoke showed a customer self-registering into the DisputeDesk organization with the restriction set. README Try it step 5 is therefore safe.

**H1: scope.** `ADMIN_SCOPE` (`provision.sh:262`) sends `{"filters":[{"or":{"queries":[{"userOrganizationId":{"id":ORG_ID}},{"resource":{"instance":true}}]}}]}`. I checked each part against the source:
- `ListAdministratorsRequest.filters = 3` (`internal_permission_service.proto:96-106`).
- `AdministratorSearchFilter.user_organization_id = 4` is an `IDFilter{id}` (`query.proto`, `filter.proto:79`). It maps to `NewAdministratorUserResourceOwnerSearchQuery`, which compares `UserResourceOwnerCol` for equality.
- `resource = 7` with `ResourceFilter.instance` (`const: true`) maps to `NewMembershipIsIAMQuery`.
- `or = 10` is `OrFilter{queries}`, and it maps to `NewOrQuery` (`internal/api/grpc/internal_permission/v2/query.go:170-176`).

The JSON field names match. The smoke showed foreign INITIAL and ACTIVE organization owners left untouched with exit 0, so a foreign organization can no longer fail the deploy. See L3 for what this scope leaves out.

**M1: the list was read, and read whole** (`provision.sh:273-277`).
- `ListAdministratorsResponse.pagination` is a `filter.v2.PaginationResponse` with `uint64 total_result` (`filter.proto:64-70`). proto3 JSON writes uint64 as a string, and the smoke saw `"4"`. `tonumber` reads both a string and a number.
- A missing `pagination` or `administrators` makes `tonumber` fail on null. `jq -e` then exits non-zero, and the step logs and returns 1. The "another shape" and "empty answer" cases cover this.
- The equality is also the right check for a second reason. `SearchAdministrators` (`internal/query/administrators.go:167-178`) counts in SQL and then drops the entries the caller may not read (`administratorsCheckPermission`). Any dropped entry makes `totalResult` larger than the rows returned, so a caller that can see less than everything also fails closed.
- Requiring `PROVISIONER_ID` in the list proves the list was read. `PROVISIONER_ID` comes from `/auth/v1/users/me` `.user.id`, read with `jq -e`.

**M1: the postcondition** (`provision.sh:289-290`). A second call to `active_human_admins` re-reads the list and each user, and `exit 1` names any user still active. The marker is never written.
- The switch from ListUsers to `GetUserByID` is justified and correct. `GetUserByID` calls `GetUserByIDWithPermission(ctx, true, ...)` (`user/v2/user_query.go:15`), and `shouldTriggerBulk` runs `triggerUserProjections` before the read (`query/user.go:378-383`). `SearchUsers` does not trigger them (`query/user.go:613-640`). Without that, the second reading could see the old ACTIVE state and fail every deploy.
- `HUMAN_NOT_INACTIVE` ends in `error(...)` for an answer that has neither `human` nor `machine`. A malformed answer therefore fails instead of meaning "nothing to deactivate". This falls under M1 ("an unexpected response shape deactivates nothing").
- Failure propagation holds under `set -euo pipefail`. Every fallible line inside the function has an explicit `|| return 1`. Bash clears `-e` inside command substitutions but keeps `pipefail`. The caller's `humans=` assignment from the function is a plain assignment, so `-e` stops the script.
- The first-page `# shortcut:` is removed, as the fix list allows, because the limit now fails closed with a named log line.

**Check 12** (`infra/terraform/README.md:366-399`).
- The snippet runs from `/opt/disputedesk` with the same `docker compose -f … -f infra/deploy/aws/compose.yml exec` form as check 11. It uses the container's own `PROVISION_*` variables, all present in the AWS render, and the image's pinned bash, curl and jq.
- The PAT reaches curl as a header from a process substitution with builtin `printf`. It is never on an argv, and it is never printed.
- `set -euo pipefail` plus `--fail-with-body` makes any failed call stop with a visible error, never print nothing.
- The expected output is right for both renders. FirstInstance gives `admin` (human) and `provisioner` (machine), with the same settings in both compose files. The provisioner adds `operations-tools` (machine, no administrator role), and the fixture logins are humans with no role. The sort orders hold: `admin@…` sorts before `provisioner`, and `operations-tools` before `provisioner`.
- The builder ran the snippet verbatim against the local container.
- The check fails safe where the live state differs from the expected output: a foreign organization left over from H1, or a lagging ListUsers projection. Either one blocks publishing Mailpit instead of letting it through. It closes round 0's L2 and security low 1.

### New tests

The tests now run the script's own code instead of matching its text. `RunProvisionSectionAsync` cuts one `# ---- <name>` section out of `provision.sh` and runs it with the script's real `log` and `rpc`, plus a recording stand-in for `call`. A missing answer makes that stand-in fail, as a failed call does. This is a stronger proof than the text tests it replaces (`DeactivateHumanAdminsStep`, `RunScriptFilterAsync`). The class summary explains it, and a renamed header fails with a clear message.

- `…PublicOrganizationRegistration_IsClosedLocallyAndInAws` asserts the exact call and body. Break 1 proves it fails without the call.
- `…InScope_AreDeactivated_AndAFreshReadingFindsNoneActive` asserts:
  - the exact scoped request (break 2);
  - one read per unique user, in sorted order;
  - active and locked users deactivated, inactive and machine users left (break 7);
  - the second reading;
  - the final log line.
- `…StillActiveAfterwards_FailsTheRun` (break 5), `…NotWholeOrWithoutTheProvisioner…`, four cases (breaks 3 and 4), and `…NeitherHumanNorMachine…` (break 6) each sit on the failing side of their bound. The in-scope test sits on the passing side (7 of 7, provisioner present).
- `…AreLeftAloneLocally` (break 8) proves that no call is made when the setting is false.

Every new test would fail without its change.

### Scope of the round

Nothing outside the fix list. Each change serves an item:
- `ME` is read once, to get `PROVISIONER_ID` (M1).
- `GetUserByID` replaces ListUsers so the postcondition can see the deactivations (M1).
- The shape error is part of M1.
- The section harness lets the new behaviour be tested.
- The docs lines in `infra/README.md`, `infra/terraform/README.md` and the AWS override's comment describe the new behaviour.

There is no new package, image or setting beyond the DefaultInstance key the fix list names.

### Pass 2: simplicity and standards

- `active_human_admins` is about fifteen plain lines, used twice (deactivate, then verify). The two jq filters are named and commented. Reading one user per call is a cold path with about four users, and the projection trigger justifies it.
- Each mechanism follows a documented source:
  - Zitadel Admin API `SetRestrictions` and the `DefaultInstance.Restrictions` setting.
  - InternalPermissionService v2 `ListAdministrators` filters and `PaginationResponse.total_result`.
  - UserService v2 `GetUserByID` and `DeactivateUser`.
  - proto3 JSON mapping (`protobuf.dev/programming-guides/json/`).
  - OWASP ASVS 5.0 V6.3.2 (bootstrap accounts disabled) and failing securely.
- No home-grown mechanism.

### Earlier findings

| # | Status |
|---|---|
| L1 (first page fails open) | **Closed.** `totalResult` must equal the rows returned, and the provisioner must be on the list. Both are tested. |
| L2 (check 12 vacuous) | **Closed.** Check 12 is now an API check with exact expected output. |

### New findings

| # | Severity | File:line | Finding | Fix |
|---|---|---|---|---|
| L3 | low | `infra/images/zitadel/provision.sh:262` | **The scope misses an administrator role on the DisputeDesk organization or project held by a user of another organization.** `userOrganizationId` matches the user's own organization, and `resource.instance` matches only instance roles. A foreign human holding `ORG_OWNER` on DisputeDesk (`resource.organizationId`) is therefore never deactivated. Mailpit receives every instance mail, so a reader could reset that user's password. Only an existing administrator can grant such a role, public organization registration is now closed, and check 12 lists every administrator unscoped before Mailpit is published. That is why this is low. The builder states the gap as an assumption. | Add `{"resource":{"organizationId":"$ORG_ID"}}` to the `or`. Optionally also add `{"resource":{"projectId":"$PROJECT_ID"}}`, which is known by then. Or leave it to the lows batch with check 12 as the guard. |

### Status (fix round 1)

```json
{"status": "approve", "record": "docs/verification/aws-admin-lockdown-review.md", "findings": [{"severity": "low", "file": "infra/images/zitadel/provision.sh", "line": 262, "issue": "Lock-down scope (userOrganizationId OR resource.instance) misses an administrator role on the DisputeDesk organization or project held by a user of another organization; such a human would stay active while Mailpit can reset its password. Mitigated: only an existing administrator can grant it, org registration is closed, check 12 lists every administrator unscoped.", "fix": "add {\"resource\":{\"organizationId\":\"$ORG_ID\"}} (and optionally projectId) to the or filter, or leave to the lows batch with check 12 as the guard"}], "assumptions": ["Live AWS state unknown; check 12 gates publishing Mailpit", "Smoke results (fresh instance, register 200, register/org 404/409, scoped list, postcondition forced failure) read from the build report, not reproduced"], "open_questions": [], "conflicts": []}
```

## Capture (after the merges of #42 and #43)

### Escapes

| Finding | Caught by | Should have been caught by | Category | Countermeasure |
|---|---|---|---|---|
| Publishing the Mailpit login also exposes the Zitadel first-instance admin's password-reset email: any reader could take over the identity provider's owner account | readme-landing reviewer | the supervisor's plan for readme-landing, and story 7.4's pre-build security, which put the admin's mail in Mailpit | auth | aws-admin-lockdown deactivates every human administrator on AWS on each deploy; check 12 and the one-time sweep before publishing |
| Public organization registration is open on AWS: an anonymous visitor becomes an `INITIAL` org owner, which `DeactivateUser` refuses, so every deploy would fail | security (round 0) | the aws-admin-lockdown builder, and the AWS environment's security review | auth | closed with the instance restriction (running and fresh instances); scope limited to DisputeDesk and the instance |
| The lock-down failed open on an unexpected response shape and read only the first page, newest first | security (round 0), reviewer and verifier as lows | the aws-admin-lockdown builder | input boundary | the step requires the provisioner in a whole list and re-reads afterwards, failing on any active human administrator |
| A conflict with #41 in the same test file after the review | supervisor at push time | dispatch collision check (two changes to `provision.sh` and `PlatformScriptTests.cs` in flight) | environment or tooling | fix round 2 merged main; no behaviour change |

### Metrics

| Fix rounds | Full-suite runs | Stages | Notes |
|---|---|---|---|
| 2 (H1 and M1; then the merge with main) | 3 (platform: 448, 457, 465 tests) | builder x3; reviewer x2; verifier x3; security x2 (one resumed after its turn limit) | `aws-admin-lockdown` merged as `c024fde` (#42), deployed 2026-11-10 12:08 UTC; check 12 and the event sweep clean |
| 0 | 0 (docs only; link, infra-path and secret checks) | reviewer x1 | `readme-landing` merged as `bcae4e5` (#43), published only after #42's deploy and sweep |
