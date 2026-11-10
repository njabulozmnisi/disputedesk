# Verification: aws-admin-lockdown

Round 0 (first build, 2026-11-10): no high or medium finding; two low findings (first page of ListAdministrators read only, marked as a shortcut; AC5 smoke-only). Astra said VERDICT: FAIL on the page limit, ranked low by the verifier; all 7 breaks failed as expected. Superseded by this record, which covers fix round 1; the page limit is now closed (M1).

## Fix round 1 (2026-11-10)

- Commit: 566a89f46f03f534cb697915b19513a9d697a880, uncommitted diff; scripts/diff-hash.sh gives aab58136562c now.
- Diff hash: the build report's cf1b6fde0235 is reproduced exactly when the supervisor's docs/production-path.md edit is excluded. Not a finding.
- Verifier: gpt-6-astra via GitHub Copilot CLI (the fixes list was given as the story file, as docs/backlog has no story file for this slug), then ranked by the verifier (Claude).
- Results files: present; TRX 457 passed, 0 failed, 0 skipped (platform-run.log agrees). Suite not re-run.

### Astra's result (VERDICT: PASS)

All criteria proved: H1 (existing instance, fresh instance, scope), M1 (provisioner required, pagination, postcondition, unexpected user shape), local administrator left alone, invalid configuration refused, check 12. Builds clean (backend and platform), dotnet format, shellcheck, floor guard clean, OpenAPI matches, frontend unchanged. Standards: none.

### Fix list check

| Item | Fixed | Proved by | Break |
|---|---|---|---|
| H1 existing instance | yes | ProvisionSection_PublicOrganizationRegistration_IsClosedLocallyAndInAws | 1 |
| H1 fresh instance (DEFAULTINSTANCE restrictions key) | yes | ComposeRenderTests.Zitadel_PublicOrganizationRegistration_IsClosedOnANewInstance | 9, 10 |
| H1 scope to DisputeDesk and instance | yes | ProvisionSection_HumanAdministratorsInScope_... (exact filtered request) | 2, 7 |
| M1 provisioner in list, totalResult vs rows | yes | ProvisionSection_AdministratorListNotWholeOrWithoutTheProvisioner_... | 3, 4 |
| M1 post-deploy re-list | yes | ProvisionSection_HumanAdministratorStillActiveAfterwards_FailsTheRun | 5 |
| M1 unexpected user shape | yes | ProvisionSection_UserThatIsNeitherHumanNorMachine_FailsTheRun | 6 |
| M1 first-page shortcut removed | yes | provision.sh holds only the font/logo shortcut line | n/a |
| Check 12 as an API check | yes (docs) | infra/terraform/README.md check 12; run by the builder in the local zitadel container | docs only |

### Breaks (scripts/run-breaks.sh, exit 0)

All 13 failed as expected (1 registration closed; 2 and 7 scope; 3 and 4 provisioner and pagination; 5 postcondition; 6 user shape; 8 local left alone; 9 and 10 fresh-instance key; 11 and 12 AWS-only render; 13 fail-closed marker).

### Other checks

- scripts/floor-guard.sh: clean.
- Non-standard mechanisms: none (Zitadel Admin API SetRestrictions, v2 ListAdministrators, ListUsers, DeactivateUser; documented DefaultInstance restrictions key).
- Not verifiable here (QA's or CI's, not failed): the live-instance smoke (anonymous POST to /ui/login/register/org giving 404 and 409, customer self-registration inside DisputeDesk, the check 12 snippet) is builder-reported; the live AWS instance changes at the next deploy.

Result: no high or medium finding, no low finding.

## Fix round 2 (2026-11-10): the reviewed change brought up to date with main

- Commit: 6dd7df61060139bfa04ebf07d65039f18a18e623 (origin/main), uncommitted diff; scripts/diff-hash.sh against origin/main gives 231410232611, equal to the build report's diff_hash. 7 files, 354 insertions, 7 deletions, none in backend or frontend.
- Results files: present; TRX 465 total, 465 passed, 0 failed (457 of round 1 plus the 8 from #41). Suite not re-run.
- Merge check: against origin/main the only removed line in PlatformScriptTests.cs is the class summary's last line ("/// activation.", extended by one sentence), the only conflict resolution. The removed lines in ComposeRenderTests.cs are the round 1 edits (the provision environment count and its name list gaining PROVISION_DEACTIVATE_HUMAN_ADMINS). Test method lists compared by name: PlatformScriptTests 29 on origin/main, 30 at round 1 (5aa5bcb), 36 now; ComposeRenderTests 36, 38, 38. No method from either side is lost; InlineData lines 16 on origin/main and at round 1, 22 now.
- provision.sh: #41's refresh_logo step (both logos) is intact; the lock-down sections (registration closed, human administrators deactivated, fail-closed re-read) sit before the client-settings step; the marker is removed at the start and touched last, right before "done".
- Breaks (scripts/run-breaks.sh, exit 0): all 13 failed as expected, as in the builder's breaks-run.log.
- scripts/floor-guard.sh: clean.
- Non-standard mechanisms: none new (unchanged from round 1).

### Astra's result (gpt-6-astra, run in a throwaway copy so the builder's tree and hash stay untouched; the fixes list as story file)

VERDICT: FAIL, on two medium findings. Builds, dotnet format, shellcheck, frontend checks and OpenAPI drift passed. Ranked by the verifier (Claude):

- Frontend test evidence absent: not a finding. Astra compared against local main, which is behind origin/main in that copy, so the frontend changes of #39 and #40 showed up. This change touches no frontend file, and the build report says why Vitest was not run.
- Empty user response treated as not active (provision.sh, active_human_admins, GetUserByID piped to jq -r): low. An HTTP 200 with an empty body is not a response Zitadel's Connect JSON returns, a missing user object makes jq error, and call already fails on HTTP >= 400. The code is unchanged from round 1, which was verified and reviewed. Hardening (reject an empty result) can go in the lows batch.
- Customer self-registration smoke and check 12 are builder-reported, QA's or CI's (not failed), as in round 1.

Result: no high or medium finding; one low finding.
