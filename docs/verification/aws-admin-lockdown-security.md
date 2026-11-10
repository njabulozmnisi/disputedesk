# Security: aws-admin-lockdown

Security review of the uncommitted change in the builder worktree (diff hash fd1047356b23), checked against Zitadel v4.20.0 source, Mailpit v1.32.0 source and `infra/images/gateway/templates-aws/nginx.conf.template`. Verdict: **findings**: one high, one medium, two low. The high finding goes to a human for a decision before merge.

Scanners: `scripts/secret-scan.sh` found no leaks. `scripts/security-local.sh` (Trivy fs and config, HIGH/CRITICAL) exited 0. `shellcheck infra/images/zitadel/provision.sh` exited 0. No packages were added.

| Sev | Finding | Action |
|---|---|---|
| high H1 | Anyone on the internet can break every AWS deploy through the new step, and anyone who reads the public inbox can become a Zitadel administrator | fix round, escalated |
| medium M1 | The step has no postcondition. It reads only the first page, and an unexpected response shape lets it do nothing and still log "no human administrator can sign in" | fix round |
| low L1 | Post-deploy check 12 is too weak to gate publishing the Mailpit login. It checks only `admin` and cannot detect an earlier compromise | lows |
| low L2 | Anyone with the published Mailpit login can use Mailpit's Send API and delete API | lows |

## H1: public organization registration (high)

Zitadel's default instance restriction `DisallowPublicOrgRegistration` is unset (cmd/defaults.yaml v4.20.0 l.1497; nothing in the repo sets it). The identity origin forwards everything except `/ui/console` (`location /`, nginx.conf.template l.218). So `GET`/`POST /ui/login/register/org` is open to anonymous visitors, with no auth request needed (internal/api/ui/login/register_org_handler.go l.40-96). It runs `SetUpOrg(..., allowInitialMail=true)`. That makes the registrant `ORG_OWNER` of a new organization (internal/command/org.go l.124-150, `orgAdminRoles` returns `RoleOrgOwner`). Because the registrant's email is unverified, `shouldAddInitCode` adds an init code and the user's state is `INITIAL` (internal/command/user_human.go l.284, l.432; user_v2_model.go l.227).

1. **Deploy denial of service, introduced by this diff, no inbox needed.** At the next deploy, `ListAdministrators` returns that `ORG_OWNER`, because an IAM_OWNER lists the members of every organization. `HUMANS_TO_DEACTIVATE` selects it, since `USER_STATE_INITIAL` is not `USER_STATE_INACTIVE`. `DeactivateUser` then fails with `Errors.User.CantDeactivateInitial` (internal/command/user_v2.go l.77). Under `set -euo pipefail` the hook exits 1 and the marker is never written. Zitadel is never healthy, so the api and the gateway never start: `--wait` fails and the demo is down. This repeats on every deploy until someone removes the organization by hand with the PAT. If that user sorts before `admin`, the run stops before `admin` is deactivated. The builder's assumption "a human administrator in state INITIAL (none exists)" does not hold, because an anonymous visitor can create one in two requests.
2. **An administrator role for any reader of the inbox.** Once the inbox is public, the registrant reads the init-code mail, completes the user and signs in. They then hold `ORG_OWNER`, an administrator role, until the next deploy. That deploy either deactivates them or, with another `INITIAL` registration, fails as in item 1. The whole Zitadel management API is reachable at the identity origin. The console is 404, but a token can be obtained through the OIDC endpoints, and a machine user with a PAT can be created in the attacker's own organization, which the step never deactivates because it skips machines. The reach is the attacker's own organization: no `dispute.*` grant, no DisputeDesk-org user, and no instance setting. Executions and v1 actions are bounded by `HTTPClient.DenyList`, whose v4.20.0 default blocks 10/8, 127/8 and 169.254/16 (cmd/defaults.yaml l.1128), so there is no server-side request forgery (SSRF) into the Compose network. Even so, it defeats the purpose of the lockdown: "no human administrator can sign in", as stated in the log line and in the Terraform README, is false.

Fix, using the documented mechanisms. Close public organization registration with Zitadel's instance restriction: Admin API `SetRestrictions` (`PUT /admin/v1/restrictions`, `{"disallowPublicOrgRegistration": true}`, proto/zitadel/admin.proto l.4428), called idempotently from `provision.sh` because the instance already exists. Add `ZITADEL_DEFAULTINSTANCE_RESTRICTIONS_DISALLOWPUBLICORGREGISTRATION: "true"` for a fresh instance. Do this in AWS at least. Locally it can stay as it is, or be applied on both, since nothing uses organization registration. Also scope the step so that a user outside the DisputeDesk organization cannot fail it: `ListAdministrators` has a `user_organization_id` filter and a `resource` filter (internal_permission/v2/query.proto l.93-118). Add a test that a foreign `INITIAL` administrator neither stops the run nor is left unhandled. ASVS 5.0 V8.2 (function-level authorization: only explicitly permitted consumers) and V6.3.2 (bootstrap and default accounts disabled).

## M1: no postcondition, first page only, fail-open on shape (medium)

- `ADMIN_IDS` and `HUMANS_TO_DEACTIVATE` use `[]?` and `.result[]?`. If a future Zitadel renames a field, both produce empty lists. With empty `inUserIdsQuery`, nothing is deactivated, yet the step still logs "no human administrator can sign in" and the run passes. The PlatformTests feed in fixed JSON answers, so an image bump would not catch it.
- Pagination: `ListAdministratorsRequest.pagination` defaults to limit 100, sorted by creation date **descending** (filter/v2/filter.proto l.46-60). The oldest memberships, which belong to `admin` and the provisioner, come last. Once more than 100 memberships exist (H1 makes this possible for anyone), `admin` falls off the first page. On the live instance this matters only if the Mailpit login is published before the first deploy with this step, or if `admin` is ever reactivated. But the "shortcut" comment's assumption ("the instance has two administrators") is under attacker control while H1 stands.

Fix: fail closed with a postcondition. Require the admin list to contain the provisioner itself, which is always an administrator, so an empty or misread list fails. Fail when `pagination.totalResult` exceeds the number of rows returned, or page through them. After deactivating, list again and fail if any in-scope human administrator is not `USER_STATE_INACTIVE`. ASVS 5.0 V16.5 (errors handled so that the control fails securely).

## L1: check 12 cannot gate publishing (low)

Check 12 shows only that `admin` stops at the login-name step. Run the following once, with the PAT from the instance, after the deploy and **before** publishing:
- `ListAdministrators` lists exactly the provisioner (machine, active) and `admin` (inactive).
- `ListOrganizations` lists only DisputeDesk, and `ListUsers` shows no machine user other than `provisioner` and `operations-tools`.
- Zitadel events for `admin` (Admin API `ListEvents`, aggregate = admin's ID) show no `user.human.password.changed`, `user.human.password.code.added`, `user.human.email.changed`, MFA, passkey or PAT event since the instance was created.
- No organization `member.added` or instance `member.added` beyond the two FirstInstance users.

Add these to the README check or to a script. Machine administrators are skipped by design, so persistence from an earlier compromise (a new machine user with a PAT) would survive the lockdown, and only this check finds it. Unverified (Zitadel source): `OIDCSessionByRefreshToken` checks only for organization deactivation, so a refresh token issued to `admin` before deactivation may still mint access tokens. This matters only after an earlier compromise, and the event check above covers it. ASVS 5.0 V6.3.2.

## L2: Mailpit API for readers (low, outside the diff)

Mailpit falls back to the UI credentials for `POST /api/v1/send` when `MP_SEND_API_AUTH` is unset (server/server.go v1.32.0 l.302-340). Any reader can therefore inject mail into the shared inbox, for example a fake "reset your password" link from `noreply@disputedesk.test` aimed at other readers. Any reader can also delete every message, which denies other visitors their OTPs. No relay is configured, so nothing is sent onward. SMTP (1025) has no published port in AWS (`ports: !reset []`), the gateway proxies only 8025, and the security group admits only CloudFront on port 80, so `MP_SMTP_AUTH_ACCEPT_ANY` is reachable only from containers. Fix: set `MP_SEND_API_AUTH` to its own secret from SSM (Mailpit, "Send API" authentication), and accept deletion as a property of a shared demo inbox.

## Answers to the brief

1. **Deactivation of `admin`: closed for every unauthenticated path checked.** Hosted login v1: `checkLoginName` refuses an inactive user (auth_request.go l.825), and `userByID` allows only active or initial users (l.1747), so the password and reset steps are never reached. A v2 password reset, passkey registration link, invite code or email change requires the user themself or `user.write` (`checkPermissionUpdateUser`, user_v2_password.go l.52; permission_checks.go l.66-80), so no anonymous caller can create a code. Even with a reset code, `setPasswordCommand` accepts an inactive user but every sign-in still refuses it. Session API: `gethumanWriteModel` requires `UserStateActive` (session.go l.275; seen live: `Errors.User.NotActive`). Existing v2 access tokens are invalidated, because `checkSessionNotTerminatedAfter` counts `user.deactivated` (query/access_token.go l.204-212). Login v2 is not deployed (`LOGINV2_REQUIRED=false`, no login container). No IdP is configured. `ReactivateUser` requires `user.write` with no self-service (user_v2.go l.108). Deactivation, not deletion, is the right call: it is reversible with the PAT, and it is ASVS 5.0 V6.3.2's "disabled". The other gaps are H1 (new administrators) and L1 (an earlier compromise).
2. **Other privileged paths.** ORG_OWNER on the DisputeDesk organization is covered, because `ListAdministrators` includes organization, project and project-grant members. A self-registered user cannot get `dispute.*`: provision.sh grants it only to `operations-tools`, and no action or execution grants it automatically. The Api also accepts operations calls only from a `sub` in `Operations:AllowedClients` (OperationsActorHandler.cs l.28). The operations machine user's secret and the IAM_OWNER PAT (`/zitadel/bootstrap/admin.pat` on the instance's named volume) are never mailed and never served. The instance setting that does allow self-registration into a privileged role is public organization registration (H1). Mailpit: see L2.
3. **The window.** Until the deploy that carries this change, `admin` is active with its SSM password and no MFA, and anyone who holds the Mailpit login can take it over through the reset mail. Deploy first, run check 12 and the L1 list, then publish. A rollback does not reactivate `admin`, because its state lives in RDS. What cannot be checked from the repo is whether the Mailpit login has already leaked. The L1 event check answers that.
4. **Fail-closed handling.** A mistyped value is refused before any call, and the test proves it. Failures from `rpc`, the pipe and the loop all stop the run (pipefail and errexit in the loop's subshell), so the hook fails closed. But it fails closed on input an attacker controls (H1), and it fails open on an unexpected response shape and on pagination (M1).

## Residual risk of the decision (not a finding against this diff), for the human

Once the inbox is public, anyone can reset any self-registered visitor's password or any fixture login's password, and can read linking OTPs. That reaches only synthetic customers, which was accepted in P50 and in the 2026-11-10 decision. However, a visitor who registers with a real email address publishes that address, which is personal information. A line on the README or the register page telling visitors to use a made-up address would avoid this.

## Fix round 1 (2026-11-10)

Re-review of the fix round in `.claude/worktrees/agent-a9df08ad4b7d9d358` (the whole uncommitted change against 566a89f; the builder's diff hash is cf1b6fde0235), checked against a sparse checkout of Zitadel v4.20.0. Verdict: **pass**. H1 and M1 are closed. Two lows remain open: L1, the part of the round-0 low that check 12 still does not cover, and L2, which this round left out. One new low, L3. There is no high, so nothing needs escalating.

Scanners, run in the worktree: `scripts/secret-scan.sh` found no leaks (exit 0). `scripts/security-local.sh` (Trivy fs and config) exited 0. `shellcheck infra/images/zitadel/provision.sh` exited 0. No packages were added. I read the builder's results rather than re-running them: the TRX file shows 457 of 457 passed, and `TestResults/breaks-run.log` shows all 13 breaks failing as expected (exit 0).

| Sev | Finding | Status |
|---|---|---|
| high H1 | Public organization registration | closed |
| medium M1 | No postcondition, first page only, fail-open on shape | closed |
| low L1 | Check 12 now finds foreign organizations, extra machine users and every administrator. It still cannot see `admin`'s events, a credential added to `provisioner` or `operations-tools`, an IdP, an action or an SMTP change | open, lows (the remainder is the one-time sweep below) |
| low L2 | Mailpit Send and delete API for readers | open, lows (not in this round) |
| low L3 | `ORG_ID` is read with `jq -r`, so a missing `resourceOwner` turns into the string "null" and shrinks the scope without failing | new, lows |

### H1: closed

- **Existing instance.** `call PUT /admin/v1/restrictions '{"disallowPublicOrgRegistration":true}'` runs at the top level under errexit at every start, so a refusal fails the start. Setting an unchanged value pushes no event (`restrictions_model.go` l.62; `SetInstanceRestrictions` pushes only when there are commands), which makes it idempotent. Both handlers check the restriction themselves: GET answers 404 and POST answers 409 (`register_org_handler.go` l.40-68). So a different route to the same handler cannot get round it. On AWS the gateway is the only public way in, and it starts only once zitadel is healthy, which needs the marker written after this call. So during a deploy there is no window before the call takes effect.
- **Fresh instance.** `ZITADEL_DEFAULTINSTANCE_RESTRICTIONS_DISALLOWPUBLICORGREGISTRATION` is the documented key (`cmd/defaults.yaml` l.1494-1497, under `DefaultInstance.Restrictions`). It is set in both Compose files, and a render test pins it.
- **No other anonymous path creates an organization or an administrator.** Only the login v1 org-registration handlers read the restriction (grep over `internal/`). Every other path is closed by a permission check instead:
  - Org v2 `AddOrganization` and v2beta `CreateOrganization`: the interceptor requires `authenticated`, then `CheckPermissionOrganizationCreate` requires `org.write` on the new organization's own ID (`permission_checks.go` l.171). Only an instance-level membership (IAM_OWNER, IAM_ORG_MANAGER) has that. A self-registered customer has no membership at all (`getUserPermissions`: "membership not found").
  - Management v1 `AddOrg` requires `org.create`. That permission is held by IAM_OWNER, IAM_ORG_MANAGER and SELF_MANAGEMENT_GLOBAL (`defaults.yaml` l.1647, l.1754, l.2088). SELF_MANAGEMENT_GLOBAL is never a default: it exists only as an org-member role that someone assigns explicitly (`member_roles.go` l.29). Admin `SetUpOrg` requires `iam.write`.
  - Login v1 self-registration (`/ui/login/register`) creates a DisputeDesk user with no membership (builder smoke). Login v2 is not deployed. Its API needs IAM_LOGIN_CLIENT, which nobody holds, and check 12 would list such a machine user.
  - Of the login's external-IdP paths, only `ensureSupportUserInstanceMembership` adds an instance member (`external_provider_handler.go` l.922-960). It needs an instance-level ZITADEL IdP, and none is configured. Sweep D below confirms that none was added.
  - Invite, password-reset and passkey codes need `user.write` or the user themself. Organization, instance, project and project-grant members need `*.member.write`, and project grants need `project.grant.write`. Executions and targets need `action.*.write`. All of these are administrator-only. No `SystemAPIUsers` are configured.
- **Scope.** The filter is `or(userOrganizationId = DisputeDesk, resource.instance)`. Its field names match `internal_permission/v2/query.proto` (l.92-131, l.165-204), and `query.go` maps them to `NewAdministratorUserResourceOwnerSearchQuery` and `NewMembershipIsIAMQuery`. A foreign registrant is out of scope, so it cannot fail the deploy (smoke: a foreign INITIAL owner was left alone and the run exited 0). That leaves one kind of privileged human the step does not cover: a user of another organization who holds a role on the DisputeDesk organization, its project or a grant of it. Only an administrator can create one, and there is no anonymous route to that (above). Check 12's unscoped `ListAdministrators` lists every such user, so they are verified absent before publishing. After that, the only administrator credential is the provisioner's PAT on the instance volume. So nothing that matters is outside the scope. Defence in depth, not a finding: adding `{"resource":{"organizationId":ORG_ID}}` to the `or` would cover that case too.
- **Leftovers from before this deploy.** Organizations created through H1 before this deploy stay, and their owners can still sign in to them. Inside their own organization they can create apps, and any instance user can sign in to such an app, DisputeDesk customers included, unless its project requires access. That would let them collect customers' profile claims (synthetic data). They get no DisputeDesk role. Remove them before publishing. `DeleteOrganization` is irreversible on a deployed environment, so the human approves it.

### M1: closed

- A single `jq -e` requires both `totalResult == rows` and the provisioner on the list. If the field is missing, `null | tonumber` fails and the function returns 1. The tests cover an empty answer, another shape, a missing provisioner and more than one page, and each one fails before any user is read. Above 100 role assignments the step now fails closed with a named log line (there are 4 today), so removing the first-page shortcut comment is correct.
- The postcondition is a second `active_human_admins` run, and the run fails if it finds anyone still active (test, and the builder's smoke with `DeactivateUser` made a no-op).
- Per-user read: a user that is neither human nor machine raises a jq error, and so does a missing `.user`. A human answer with no `.state` counts as not inactive. If that user is already inactive, `DeactivateUser` then refuses, so this case fails closed too.
- Error propagation: bash clears errexit inside command substitutions. So `active_human_admins` handles every failure itself (`|| return 1`, with pipefail inherited). `humans=$(…)` and `still_active=$(…)` are plain top-level assignments, so a failed function stops the script and the marker is never written.

### L1 remainder: what check 12 shows (low)

Check 12 shows foreign organizations (`ListOrganizations`), machine users in every organization (v2 `ListUsers` is instance-wide for an IAM_OWNER, filtered in SQL), and every administrator of the instance, an organization, a project or a grant (unscoped `ListAdministrators`), along with `admin`'s state. The expected output is exact, so a list longer than one page can never match, and a truncated list therefore fails the check.

It does not show:
- `admin`'s events: sign-ins, resets, MFA added.
- A new PAT or key on `provisioner`, or a new secret or key on `operations-tools`. This is the persistence an earlier IAM_OWNER compromise would leave behind, and the lockdown never touches it because it skips machines.
- IdPs, v1 actions, executions or targets, app redirect or secret changes, or SMTP changes.

The fix list gave the event check to the supervisor as a one-time step, which is below. Keep this low so that sweeps C and D become part of check 12 and repeatable. `ListUsers` does not bring the user projection up to date before reading, so run check 12 a minute after the deploy finishes.

### L3: `ORG_ID` read without `-e` (low)

`ORG_ID=$(jq -r '.user.details.resourceOwner' <<< "$ME")` was there before this round, but the scope now depends on it. If the field ever goes missing, `ORG_ID` becomes `null`. The filter then matches only instance roles, and the provisioner check still passes, because the provisioner is an instance administrator. A human who administers only the DisputeDesk organization would then be missed without any failure. Later, `CreateAuthorization` with organization `null` probably fails the run, but I have not verified that. The fix is `jq -er`, as `PROVISIONER_ID` already uses. ASVS 5.0 V16.5.3.

### One-time checks on AWS before publishing the Mailpit login

Order: deploy; run check 12 and see its exact output; run the sweep below; publish only if both are clean. Anything unexpected goes to the human, and so does any deletion. The sweep is read-only. It runs on the instance from `/opt/disputedesk`, inside the zitadel container, with the provisioner's token, which never leaves the container:

```sh
docker compose -f docker-compose.yml -f infra/deploy/aws/compose.yml exec -T zitadel bash -s <<'SH'
set -euo pipefail
auth(){ printf 'Authorization: Bearer %s\n' "$(tr -d '\n' < "$PROVISION_PAT_FILE")"; }
get(){ curl -sS --fail-with-body --max-time 30 --connect-to "$PROVISION_CONNECT_TO" -H @<(auth) "$PROVISION_CALL_URL$1"; }
post(){ printf '%s' "$2" | curl -sS --fail-with-body --max-time 30 --connect-to "$PROVISION_CONNECT_TO" -H @<(auth) \
  -H 'Content-Type: application/json' -H 'Connect-Protocol-Version: 1' --data-binary @- "$PROVISION_CALL_URL$1"; }
rows='(.events // [])[] | [.creationDate, .type.type, .aggregate.type.type, .aggregate.id, (.editor.userId // "")] | @tsv'

PROVISIONER=$(get /auth/v1/users/me | jq -er .user.id)
ADMIN=$(post /zitadel.user.v2.UserService/ListUsers "{\"queries\":[{\"userNameQuery\":{\"userName\":\"admin@disputedesk.$ZITADEL_EXTERNALDOMAIN\",\"method\":\"TEXT_QUERY_METHOD_EQUALS_IGNORE_CASE\"}}]}" \
  | jq -er '.result | if length == 1 then .[0].userId else error("not exactly one admin user") end')
echo "provisioner $PROVISIONER  admin $ADMIN"

echo "A. every event on admin"
post /admin/v1/events/_search "{\"aggregateTypes\":[\"user\"],\"aggregateId\":\"$ADMIN\",\"asc\":true,\"limit\":1000}" | jq -r "$rows"

echo "B. every event admin made, anywhere"
post /admin/v1/events/_search "{\"editorUserId\":\"$ADMIN\",\"asc\":true,\"limit\":1000}" | jq -r "$rows"

echo "C. organizations, administrators, machine credentials, apps, grants, actions, mail server"
post /admin/v1/events/_search '{"asc":true,"limit":1000,
  "aggregateTypes":["org","instance","project","user","usergrant","execution","target"],
  "eventTypes":["org.added","instance.member.added","instance.member.changed","org.member.added","org.member.changed",
    "project.member.added","project.member.changed","project.grant.added","project.grant.member.added",
    "project.application.added","project.application.config.oidc.changed","project.application.config.oidc.secret.changed",
    "project.application.config.api.secret.changed","user.machine.added","user.pat.added","user.machine.key.added",
    "user.machine.secret.set","user.machine.secret.updated","user.grant.added","user.grant.changed","user.reactivated",
    "user.unlocked","user.impersonated","execution.set","execution.v2.set","target.added",
    "instance.smtp.config.added","instance.smtp.config.changed"]}' | jq -r "$rows"

echo "D. instance IdPs, DisputeDesk IdPs, v1 actions, targets, executions: each must be 0"
post /admin/v1/idps/templates/_search '{}' | jq '.result | length'
post /management/v1/idps/templates/_search '{}' | jq '.result | length'
post /management/v1/actions/_search '{}' | jq '.result | length'
post /zitadel.action.v2.ActionService/ListTargets '{}' | jq '.targets | length'
post /zitadel.action.v2.ActionService/ListExecutions '{}' | jq '.executions | length'
SH
```

The endpoints and field names were read from the v4.20.0 protos: Admin `ListEvents` (`POST /admin/v1/events/_search`, permission `events.read`, limit capped at 1000, default `AuditLogRetention: 0s` so every event is still there), `ListProviders`, Management `ListActions`, and Action v2 `ListTargets` and `ListExecutions`. The event-type names come from `internal/repository/*`. Not run against AWS. If a section prints 1000 lines, page it with `from`.

How to read it:

- **A, events on `admin`.** Expected: its creation events at the time the instance was created, with an empty editor, and one `user.deactivated` whose editor is the provisioner, at this deploy. Attempts, not compromise, because anyone could cause them through the login while `admin` was active (their mail went only to the protected inbox): `user.human.password.code.added`, `user.human.password.code.sent`, `user.human.password.check.failed`, `user.locked`. **Compromise:**
  - Someone signed in or held a session: `user.human.password.check.succeeded`, `user.human.password.hash.updated`, `user.human.mfa.init.skipped`, `user.token.added`, `user.human.refresh.token.added`, `user.human.signed.out`, `user.human.externallogin.check.succeeded`, and any `…check.succeeded` under `user.human.mfa.` or `user.human.passwordless.`.
  - Someone took the account over or set up persistence: `user.human.password.changed`, `user.human.email.changed`, `user.human.phone.changed`, `user.username.changed`, `user.human.mfa.otp.added` or `.verified`, `user.human.mfa.otp.sms.added`, `user.human.mfa.otp.email.added`, `user.human.mfa.u2f.token.added`, `user.human.mfa.recoverycode.added`, `user.human.passwordless.token.added`, `user.human.passwordless.initialization.code.added` or `.requested`, `user.human.externalidp.added`, `user.metadata.set`, `user.reactivated`, `user.unlocked`, `user.impersonated`.
- **B, events `admin` made anywhere.** Any line means `admin` acted, which is a compromise unless the human knows they used it.
- **C, administrative events.** Expected at instance creation, with an empty editor: DisputeDesk's `org.added`, the FirstInstance `instance.member.added` and `org.member.added` events, `user.machine.added` and `user.pat.added` for `provisioner`, the ZITADEL project's `project.application.added` events, and `instance.smtp.config.added`. Expected with the provisioner as editor: `disputedesk-web`'s `project.application.added`, `operations-tools`' `user.machine.added`, `user.machine.secret.set` and `user.grant.added`. **Compromise:** any later line with another editor, and in particular a later `user.pat.added`, `user.machine.key.added` or `user.machine.secret.updated`, a member event, `execution.*` or `target.added`, `instance.smtp.config.changed`, an app added or changed, or `user.grant.added`. A second `org.added` with its `org.member.added` is an H1 leftover: remove it with `zitadel.org.v2.OrganizationService/DeleteOrganization` once the human approves, then run check 12 again.
- **D.** Every count must be 0. If `ListTargets` or `ListExecutions` answers with an error, the `execution.*` and `target.added` lines in C decide instead.

What the sweep cannot tell is whether the Mailpit login itself has already leaked. Even so, a clean A, B, C and D means no administrator access came of it. Optionally, and as the human's call because it destroys demo data: delete the inbox's messages before publishing, so that old reset and OTP mails are not published with it.

```json
{"status": "pass", "record": "docs/verification/aws-admin-lockdown-security.md", "findings": [{"id": "L1", "severity": "low", "summary": "Check 12 does not see admin's events, new credentials on provisioner or operations-tools, IdPs, actions or SMTP changes; make sweeps C and D part of it"}, {"id": "L2", "severity": "low", "summary": "Mailpit Send and delete API use the published UI login (not in this round)"}, {"id": "L3", "severity": "low", "summary": "ORG_ID read with jq -r: a missing resourceOwner shrinks the lock-down scope to instance roles without failing; use jq -er"}]}
```
