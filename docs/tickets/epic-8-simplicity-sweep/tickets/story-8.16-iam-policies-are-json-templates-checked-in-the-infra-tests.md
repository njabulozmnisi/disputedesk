---
id: 16
type: story
title: "IAM policies are JSON templates checked in the infra tests, and the OIDC jobs become a recorded check"
parent: epic-8-simplicity-sweep
covers: [R33, R38, R39]
after: [8]
hitl: false
risk: medium
refined: true
---

# IAM policies are JSON templates checked in the infra tests, and the OIDC jobs become a recorded check

## Description

Slice S16 of the mitigation plan (wave 3), carrying the human's decision D9 with D8 (the seven modules stay). Story 7.16's check that every IAM action and condition key exists in AWS's programmatic service reference is kept, but it reads HCL with 14 regular expressions and `extglob` patterns, refuses shapes it cannot parse and lists its own limits (INF-7). Two CI jobs mint a GitHub OIDC token on every push to `main`, decode its payload by hand in jq and feed the `sub` to a separate Terraform test, although the exact subject is already pinned by `tests/deploy_role.tftest.hcl` and a mismatch would show as an explicit `AssumeRoleWithWebIdentity` failure on the manual deploy (INF-8). This story moves each IAM policy document into a JSON file rendered with `templatefile()`, so the infra test project reads every `Action` and condition key straight from the files with `System.Text.Json`; `iam-names.test.sh` goes; the two OIDC jobs are replaced by a one-time check recorded in `infra/terraform/README.md`. No policy's meaning changes.

Human decision, 2026-11-03 (D8 and D9): D8, no, keep the seven modules (AD-28 records that they are kept ready for a second environment), so S16 applies D9 inside them. D9, yes, IAM policies as `templatefile()` JSON checked by jq, and the OIDC CI jobs replaced by a recorded one-time check; `iam-names.test.sh`'s regex HCL parsing goes, two CI jobs go, and the pinned `sub` test stays.

Human decision, 2026-11-04 (`docs/verification/epic-8-decisions.md`, 8.16): the templates are read with `System.Text.Json`, not jq; D9's "checked by jq" becomes "checked by `System.Text.Json` in `infra/tests`", with no external tool.

Human decisions, 2026-11-01 (story 7.16), unchanged: every IAM `Action` and condition key must exist in AWS's programmatic service reference, a public HTTPS JSON fetch with no credentials and a timeout on every fetch, with a deliberate-break criterion; the OIDC exception is exactly `token.actions.githubusercontent.com:aud` and `:sub`, labelled not verified against the service reference.

## Requirements

Verify: `terraform fmt -check`, `terraform validate` and `terraform test` (mocked providers) pass for `bootstrap` and `envs/dev` with every `*.tftest.hcl` unchanged; `dotnet test --project DisputeDesk.PlatformTests` passes in `infra/tests/` locally and in CI; actionlint passes; no AWS command is run and no AWS API is called.

Inputs:
- Policy templates: every `*.json.tftpl` file under `infra/terraform` (excluding `.terraform`), one IAM policy document each. Each is valid JSON as written: template placeholders (`${…}`) appear only inside JSON strings, so `JsonDocument.Parse` accepts the unrendered file. Its top-level object has exactly the keys `Version` and `Statement`, and `Version` is `"2012-10-17"`.
- Policy attributes in `*.tf` (excluding `.terraform`): a line matching `^\s*(policy|assume_role_policy|policy_document)\s*=` must continue with `templatefile(` naming a `.json.tftpl` file. The anchor keeps `block_public_policy` and the `*_protocol_policy` attributes out. No `jsonencode(` appears except the SSM document's `content`, and no `aws_iam_policy_document` data source appears.
- Action: a string or array of strings under `Statement[].Action` or `Statement[].NotAction`, bound `<service>:<name>`, `<service>` matching `^[a-z0-9-]+$`, `<name>` matching `^[A-Za-z0-9*?]+$`.
- Condition key: every key of every operator object under `Statement[].Condition`, bound `^[A-Za-z0-9._/-]+(:[A-Za-z0-9._/-]+)+$`.
- Fetched files (unchanged from 7.16), from `https://servicereference.us-east-1.amazonaws.com/`: the index `/`, `v1/global-condition-keys.json`, and `v1/<service>/<service>.json` for each service prefix named. Each fetch: HTTPS only, no redirect, no credential, cookie or extra header, 10 s to connect (`SocketsHttpHandler.ConnectTimeout`), 30 s in all, at most 2 retries; the whole check, retries included, is bounded at 5 minutes by one cancellation deadline of the test's own (the CI step's 5-minute timeout goes with the step).
- Offline (a local run with no network): the fetch fails and the test fails, naming the URL; the check never passes without the reference.

1. **Each IAM policy is a JSON template** (D9, INF-7; R38, NFR-INFRA-4)
   **Given** the policies written with `jsonencode()`: `modules/deploy-role/main.tf` (`assume_role_policy` at `:68`, `aws_iam_role_policy.deploy` at `:138`), `modules/compute/main.tf` (`assume_role_policy` at `:118`, `aws_iam_role_policy.instance` at `:143`), `modules/observability/main.tf:32` (`policy_document`) and `bootstrap/main.tf:82` (the state bucket policy)
   **When** this story lands
   **Then** each document is a `<name>.json.tftpl` file beside its `main.tf`, rendered with `templatefile()` whose variables are today's interpolated values (ARNs, the OIDC subject, the region, the account ID, the paths, the KMS key ARN), and every `*.tftest.hcl` that pins a policy (`envs/dev/tests/deploy_role.tftest.hcl` with its exact `sub` at `:52`, `compute.tftest.hcl:192`, `observability_budget.tftest.hcl:35`, `bootstrap/tests/bootstrap.tftest.hcl:60`) passes unchanged
   **And** the SSM document's `content` (`deploy-role/main.tf:92`), which is not an IAM policy, stays `jsonencode()`

2. **Names are read from the templates with System.Text.Json** (D9 as answered 2026-11-04, INF-7; R38)
   **Given** an `IamNamesTests` class in `infra/tests/DisputeDesk.PlatformTests`
   **When** it runs
   **Then** it reads every action and condition key from the templates in Inputs with `JsonDocument`, with no external tool, prints one line per distinct (name, file), and fails if a template is not valid JSON, its top-level keys or `Version` differ from Inputs, a name is outside its bound, or no action or no condition key is found across all templates together (a single template may have no condition key, as the compute trust policy has none)
   **And** it fails if a policy attribute line in a `.tf` file does not continue with `templatefile(` of a `.json.tftpl`, if `jsonencode(` appears anywhere but the SSM `content`, or if an `aws_iam_policy_document` appears, each named with its file and line; these are the only text rules over HCL, and each break in the Supervisor checks proves one

3. **Every action exists in its service's reference** (7.16 criterion 3, carried; R38)
   **Given** the index and each service file's `Actions[].Name`
   **When** the test looks up each action
   **Then** it passes when its service prefix is in the index and its name matches at least one `Name` of that service case-insensitively, with `*` meaning any characters and `?` one (AWS IAM, *Action*); an unknown prefix or unmatched name fails, naming the action and its file

4. **Every condition key exists, with the one documented exception** (7.16 criterion 4, carried; R38)
   **Given** the global keys' `Name`s and every fetched service file's `ConditionKeys[].Name`
   **When** the test looks up each key, case-insensitively
   **Then** it passes on a match, where a `${…}` segment of a reference name matches any non-empty text without `:`; otherwise it passes only if it is exactly `token.actions.githubusercontent.com:aud` or `token.actions.githubusercontent.com:sub`, printed as "not verified against the service reference"; any other key fails, naming the key and its file

5. **Every fetch is bounded, credential-free and fails closed** (7.16 criterion 5, carried; R33)
   **Given** the fetches in Inputs, made with one `HttpClient` (`AllowAutoRedirect = false`, no credentials, no cookies) and the bounds in Inputs
   **When** a fetch fails, times out, answers an HTTP error or returns a file of the wrong shape (the index an array of objects with a string `service`; the global keys an array of objects with a string `Name`; a service file with `Actions` and `ConditionKeys` arrays of objects with a string `Name`)
   **Then** the test fails naming the URL or file
   **And** when the whole-check deadline passes, the test fails naming the URL whose fetch it interrupted

6. **The check proves a misspelt name fails** (7.16 criterion 6, carried; R38, R33)
   **Given** the test's deliberate-break cases, which copy the templates into a temporary directory and run the same check on the copy with the reference already fetched: `ssm:SendCommand` to `ssm:SendComand`; `kms:ViaService` to `kms:ViaServices`; `token.actions.githubusercontent.com:sub` to `token.actions.githubusercontent.cmo:sub`
   **When** the tests run
   **Then** each copy's check fails naming exactly the misspelt name, and the real tree passes
   **And** `infra/terraform/iam-names.test.sh`, the CI step *IAM names exist in AWS's service reference* (`ci.yml:269-271`) and 8.8's named exception for the script are deleted

7. **The OIDC subject is a recorded one-time check** (D9, INF-8; R33, R38)
   **Given** the `oidc-token` and `oidc-subject` jobs (`ci.yml:467-554`) and `infra/terraform/envs/dev/tests-oidc/`
   **When** this story lands
   **Then** both jobs and the `tests-oidc/` directory are deleted; `infra/terraform/README.md`'s deploy-role row and section *8. Repository variables and first deploy* record the one-time check, `gh api repos/<owner>/<repo>/actions/oidc/customization/sub`: `use_immutable_subject` must be `true` and `sub_claim_prefix` must carry the same owner, owner ID, repository name and repository ID as the subject pinned in `tests/deploy_role.tftest.hcl` (`repo:<owner>@<owner-id>/<name>@<repo-id>:ref:refs/heads/main`, story 7.15); a mismatch stops the first deploy until the pinned subject and the module are corrected
   **And** the README says when to repeat the check: after a repository transfer or rename, or a change to the repository's OIDC subject customisation; and that the API returns configuration, not a token issued to a job, so the first deploy's `AssumeRoleWithWebIdentity` is the end-to-end proof
   **And** that pinned test stays; the job's `publish.needs` is unchanged

8. **The docs name the new check** (D9; R39)
   **Given** `infra/README.md`'s `terraform/` row (which names `tests-oidc/` and `iam-names.test.sh`) and `infra/terraform/README.md`
   **When** this story lands
   **Then** both name the `.json.tftpl` policy files, `IamNamesTests` in the infra test project and the recorded OIDC check

## Boundaries

- Must not change: any policy's effect, action, resource, principal or condition; the seven modules and their interfaces (D8); `deploy.sh`; the Terraform tests' assertions; the `terraform` job's other steps.
- Not checked, and not claimed (unchanged from 7.16): sufficiency or least privilege, IAM evaluation, whether a key applies to its action, wildcard breadth, ARN formats, principals, the managed policies attached in `modules/compute`.
- Not claimed: discovery of every possible HCL policy declaration. The guarantee is the six known policy attributes, the anchored line rule and the `jsonencode(` and `aws_iam_policy_document` bans in criterion 2; no HCL parser is added.
- No AWS account, AWS command or AWS API call; no IAM policy linter is added (the JSON files would allow one later; production path).

## Supervisor checks

Throwaway copy, each must fail naming the file and name or URL:
- `xray:PutSpans` to `xray:PutSpan` in the compute instance template.
- `logs:PutLogEvents` to `cloudwatchlogs:PutLogEvents`.
- `token.actions.githubusercontent.com:aud` to `token.actions.githubusercontnet.com:aud`.
- One template's `"Resource": ["${instance_arn}"]` written as `"Resource": ${resources}` (not valid JSON).
- A `policy = jsonencode({…})` added in any module.
- A `policy =` line whose `jsonencode(` starts on the next line; the anchored rule fails it.
- An `aws_iam_policy_document` data source added in any module.
- One template's `"Version"` changed to `"2008-10-17"`.
- The index URL pointed at `v1/no-such-file.json`.

## Build facts

- Owner: backend (backend-builder; `infra/` and `.github/` are its paths).
- Tier: Feature, infra; the security agent re-runs, because it touches IAM.
- Flags: security.
- Lane: A or B, after 8.8; independent of the backend slices.
- Before the build (supervisor, docs-only change recording D8, D9 and the human's answer of 2026-11-04): AD-28 records that the modules are kept for a second environment and that policies are JSON templates checked by `System.Text.Json` in `infra/tests`; the production path's OIDC entries match. The builder does not edit them.
- Mechanisms and sources:
  - `templatefile()`: <https://developer.hashicorp.com/terraform/language/functions/templatefile>.
  - `System.Text.Json` `JsonDocument`: <https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/use-dom>.
  - IAM policy `Version` element: <https://docs.aws.amazon.com/IAM/latest/UserGuide/reference_policies_elements_version.html>.
  - AWS programmatic service reference: <https://docs.aws.amazon.com/service-authorization/latest/reference/service-reference.html>.
  - IAM `Action` and `Condition` elements and the OIDC federation keys: <https://docs.aws.amazon.com/IAM/latest/UserGuide/reference_policies_elements_action.html>; <https://docs.aws.amazon.com/IAM/latest/UserGuide/reference_policies_elements_condition.html>; <https://docs.aws.amazon.com/IAM/latest/UserGuide/reference_policies_iam-condition-keys.html#condition-keys-wif>.
  - GitHub OIDC subject customisation API: <https://docs.github.com/en/rest/actions/oidc>.
  - `HttpClient` timeouts and redirects: <https://learn.microsoft.com/dotnet/api/system.net.http.socketshttphandler.connecttimeout>; <https://learn.microsoft.com/dotnet/api/system.net.http.socketshttphandler.allowautoredirect>.
- Packages: none.
- Touches: `infra/terraform/modules/deploy-role/`, `modules/compute/`, `modules/observability/`, `bootstrap/` (new `.json.tftpl` files, `main.tf`), `infra/terraform/iam-names.test.sh` (deleted), `infra/terraform/envs/dev/tests-oidc/` (deleted), `infra/tests/DisputeDesk.PlatformTests/IamNamesTests.cs` (new), the 8.8 harness rule test, `.github/workflows/ci.yml` (`terraform` job's IAM step; the two OIDC jobs), `infra/README.md`, `infra/terraform/README.md`.

## Pre-build

- Challenger: `docs/verification/8.16-challenge.md` (simplify).
- Review and security: `docs/verification/epic-8-prebuild-wave3.md`, sections Review 8.16 and Security read 8.16.
- Decisions: settled by the human, 2026-11-04 (`docs/verification/epic-8-decisions.md`, D9: `System.Text.Json`).
- Sofia, 2026-11-04: every `[story]` point folded; the title and file name drop "read with jq". Review: the anchored attribute rule (Inputs, criterion 2); why string placeholders over `jsonencode` (Notes); offline behaviour and `SocketsHttpHandler.ConnectTimeout` (Inputs). Security: the anchored rule with the `jsonencode(` and `aws_iam_policy_document` bans; top-level keys and `Version` pinned (Inputs, criterion 2); when to re-run the OIDC check (criterion 7). Challenger: escaping (Notes); the guarantee limited to the stated rules, no HCL parser (Boundaries); "no action or key" across all templates (criterion 2); a next-line attribute break (Supervisor checks); a whole-check deadline naming the interrupted URL (criterion 5); the API fields, the stop condition and the re-run triggers, with the configuration-not-token limit (criterion 7).

## References

- parent — docs/tickets/epic-8-simplicity-sweep/epic-8-simplicity-sweep.md, section Requirements
- plan — docs/verification/simplicity-audit/mitigation-plan.md, section 1 (D8, D9), wave 3, S16
- audit — docs/verification/simplicity-audit/infra-ci-scripts.md, INF-6 (kept, D8), INF-7, INF-8
- stories — docs/tickets/epic-7-release/tickets/story-7.15-deploy-role-trusts-the-immutable-oidc-subject.md; story-7.16-iam-names-checked-against-the-aws-service-reference.md
- architecture — docs/architecture/architecture.md, AD-28

## Notes

- Risk medium (proposed): a template that renders a different policy would change IAM; the unchanged `jsondecode` pins in the Terraform tests prove each renders as before.
- Human decisions, 2026-11-03 (D8, D9), and 2026-11-01 (7.16), as quoted in the Description; not reopened here.
- Human decision, 2026-11-04 (`docs/verification/epic-8-decisions.md`, 8.16): `System.Text.Json`, not jq; this replaces Sofia's earlier "reading names with jq".
- Decision: 2026-11-04 (Sofia) — the check lives in the infra test project (the one infra style, 8.7 and 8.8), reading names with `System.Text.Json` and fetching with `HttpClient`, so no shell harness and no external tool remain.
- Decision: 2026-11-04 (Sofia) — placeholders only inside JSON strings, so each template is valid JSON before rendering and the test reads it without Terraform; a list value is written as a JSON array of placeholders. HashiCorp's `templatefile` page advises `jsonencode` when generating JSON, because an interpolated value is not JSON-escaped; string placeholders are kept because every value is an ARN, an ID, the region, a path or the OIDC subject, made by Terraform resources or by variables with `validation` blocks (`envs/dev/variables.tf`, `bootstrap/main.tf`), and none of these holds a `"` or `\`. A value that could hold one would need `jsonencode` for that value.
