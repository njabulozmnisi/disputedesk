---
id: 16
type: story
title: "IAM action and condition-key names checked against AWS's service reference"
parent: epic-7-release
covers: [R38, R33]
after: [5, 15]
hitl: false
risk: low
refined: true
---

# IAM action and condition-key names checked against AWS's service reference

## Description

The mocked `terraform test` suite pins every IAM policy document exactly, but it cannot tell whether a name in it exists: a misspelled action such as `ssm:SendComand` would plan and pass, and fail only on the first real apply or, worse, silently grant nothing. AWS publishes every service's actions and condition keys as public JSON, the programmatic service reference, readable over HTTPS with no credentials. This story adds one script, `infra/terraform/iam-names.test.sh`, that reads every `Action` and condition key from the Terraform files, refuses any policy written in a shape it cannot read, and checks each name against that reference; it runs as a step of the existing CI `terraform` job. The script also proves it can fail: it runs the same check on copies of the Terraform with a name misspelled and expects each to be refused.

Human decision, 2026-11-01: every IAM `Action` and condition key in the Terraform policies must exist in AWS's programmatic service reference, checked in the existing `terraform` CI job with a timeout on every fetch, with a deliberate-break criterion; there is no AWS account, and no story runs any AWS command or calls AWS (the fetch is a public HTTPS download, not an AWS API call, and needs no credentials).

Human decision, 2026-11-01 (7.16 challenge): a documented exception for the GitHub OIDC provider's keys. Exactly `token.actions.githubusercontent.com:aud` and `token.actions.githubusercontent.com:sub` are accepted on the IAM User Guide's OIDC federation keys, and the check says plainly that these two are not verified against the service reference; any other unknown key fails.

## Requirements

Verify: `infra/terraform/iam-names.test.sh` exits 0 locally and as a step of the CI `terraform` job, printing each name it checked with its file; its own deliberate-break section passes (criterion 6); `actionlint` passes on `.github/workflows/ci.yml`; `shellcheck` passes on the script locally; no Terraform file changes.

Inputs:
- Scanned files: every `*.tf` file under `infra/terraform`, excluding any `.terraform` directory (the same set the job's grep steps read). The policies there are hand-written `jsonencode()` maps (story 7.5).
- Action line: a line whose first token, after leading whitespace, is `Action`, `NotAction`, `"Action"` or `"NotAction"` (case-sensitive, so the SSM document's lower-case `action` step is not read), followed by `=` or `:`. Its right-hand side must be one double-quoted literal, or a one-line `[...]` list of double-quoted literals, with at most a trailing comma after it.
- Action bound: `<service>:<name>`, `<service>` matching `^[a-z0-9-]+$` and `<name>` matching `^[A-Za-z0-9*?]+$`.
- Condition key: every double-quoted string containing `:` that is a map key, that is, followed by optional whitespace and `=` or `:`. Bound: `^[A-Za-z0-9._/-]+(:[A-Za-z0-9._/-]+)+$`.
- Policy attributes: `policy`, `assume_role_policy` and `policy_document`.
- Fetched files, from the documented base `https://servicereference.us-east-1.amazonaws.com/` (AWS Service Authorization Reference, *Simplified AWS service information for programmatic access*):
  - the index `/`, a JSON array of `{service, url, modified}`, used only to check that a service prefix exists;
  - `v1/global-condition-keys.json`;
  - `v1/<service>/<service>.json` for each service prefix the scanned files name, the URL built from that documented layout, not taken from the index.
- Fetch command: `curl -q --proto '=https' -sS --fail --connect-timeout 10 --max-time 30 --retry 2 --retry-max-time 60 -o <file> <url>`, no `-L`. Every file is fetched once per run, into one temporary directory removed on exit.

1. **The check reads every name, one line per name and file** (R38, R33; NFR-INFRA-4, NFR-INFRA-2)
   **Given** the scanned files and the rules in Inputs
   **When** `infra/terraform/iam-names.test.sh` runs on the current tree
   **Then** it prints one line per distinct (name, file) pair, an action or a condition key with the file it came from, and pins no count
   **And** it fails if `infra/terraform` is missing or the scan finds no action or no condition key, and it ends with one line saying that only names were checked

2. **A policy the rules cannot read fails the check** (R38; NFR-INFRA-4)
   **Given** the scanned files
   **When** any of the following is present: an action line whose right-hand side is not the shape in Inputs (a `${`, an expression, a multi-line list, or any leftover text after the literals); a literal or key outside its bound; an `aws_iam_policy_document`; a policy attribute whose value is not `jsonencode(` (a heredoc, `file()` or `templatefile()`); or a `Condition` whose value does not start with `{`
   **Then** the check fails naming the file and line, so no name is skipped silently
   **And** `templatefile()` on any attribute that is not a policy attribute, such as the instance's `user_data`, is not refused

3. **Every action exists in its service's reference** (R38; NFR-INFRA-4)
   **Given** the index and each service file's `Actions[].Name`
   **When** the check looks up each action
   **Then** it passes when its service prefix is in the index and its name matches at least one `Name` of that service, compared case-insensitively because IAM action names are case-insensitive, with `*` meaning any characters and `?` one character (AWS IAM, *IAM JSON policy elements: Action*)
   **And** an unknown service prefix or an unmatched name fails the check, naming the action and its file

4. **Every condition key exists in the reference, with one documented exception** (R38; NFR-INFRA-4)
   **Given** one lookup set: the global keys' `Name`s together with the `ConditionKeys[].Name`s of every fetched service file
   **When** the check looks up each key, compared case-insensitively (AWS IAM, *IAM JSON policy elements: Condition*: condition key names are not case-sensitive)
   **Then** a key passes when it matches a name in that set, where a `${...}` segment in the reference name matches any non-empty text without `:` (so `kms:EncryptionContext:PARAMETER_ARN` matches `kms:EncryptionContext:${EncryptionContextKey}`); otherwise it passes only if it is one of the literal list `token.actions.githubusercontent.com:aud`, `token.actions.githubusercontent.com:sub` (AWS IAM, *IAM and AWS STS condition context keys*, `#condition-keys-wif`), and its output line labels it "not verified against the service reference"
   **And** any other key, including the same claim under a misspelt provider, fails the check, naming the key and its file

5. **Every fetch is bounded, credential-free and fails closed** (R33; NFR-INFRA-2)
   **Given** the fetches in Inputs
   **When** a fetch fails, times out or answers an HTTP error, or a file is not the shape expected (`jq -e`: the index an array of objects with a string `service`; the global keys an array of objects with a string `Name`; a service file with an `Actions` array and a `ConditionKeys` array of objects with a string `Name`)
   **Then** the script exits non-zero naming the URL or file; every `curl` and `jq` call has an explicit failure branch, because `set -e` does not apply inside `if`, `!`, `&&`, `||` or `$( )`; names reach `jq` only through `--arg`
   **And** it sends no credential, cookie, header or AWS signature (`curl -q` ignores `~/.curlrc`; no `aws` CLI), follows no redirect, and the CI step's `timeout-minutes: 5` bounds the whole run (see Notes)

6. **The script proves a misspelt name fails** (R38, R33; NFR-INFRA-4)
   **Given** the script's final section, which copies the scanned files into a temporary directory and runs the same check function on the copy, reusing the reference directory already fetched, three times: `ssm:SendCommand` changed to `ssm:SendComand`; `kms:ViaService` changed to `kms:ViaServices`; `token.actions.githubusercontent.com:sub` changed to `token.actions.githubusercontent.cmo:sub`
   **When** the script runs
   **Then** each copy's check fails naming exactly the misspelt name, its output is captured rather than printed, and the script prints one `ok` line per copy and exits 0 only if all three failed as expected and the real tree passed
   **And** the check function takes the Terraform directory and the reference directory as arguments and reports failure by its return code, never by exiting the script; the script itself takes no arguments and always checks `infra/terraform`

7. **It runs in the existing terraform CI job** (R33; NFR-INFRA-2)
   **Given** `ci.yml`'s `terraform` job
   **When** CI runs on a pull request or on `main`
   **Then** a step named *IAM names exist in AWS's service reference*, with `timeout-minutes: 5`, runs `infra/terraform/iam-names.test.sh` after *Validate and test (mocked plans)*, and a failure fails the job
   **And** the job's permissions and secrets do not change, `publish.needs` does not change, and the comment above `publish` is corrected to say it runs after the jobs in its `needs`, which leave out `terraform` and `oidc-subject`

## Boundaries

- Must not change: any Terraform file, policy or test; the other `terraform` job steps; `publish.needs`; any other job, apart from the one-line comment above `publish` (criterion 7).
- Not checked, and not claimed: whether a policy is sufficient or least-privilege; how IAM evaluates it; whether a condition key applies to the action beside it; how much a wildcard action grants; resource ARN formats; principals; and the AWS managed policy names attached in `modules/compute`. These wait on a real account (P46).
- No new package, tool or action: `bash`, `curl` and `jq` are already on the `ubuntu-24.04` runner and used by the other `infra/` scripts. No HCL parser, `hcl2json`, Parliament, Access Analyzer or plan JSON.
- No cache of the reference committed to the repository: the check reads the live reference, which AWS updates within 24 hours of a change.

## Supervisor checks

Deliberate-break proofs, run by the supervisor in a throwaway copy, not by the verifier. Each run must fail, naming the file and line, name or URL.

- Misspelt action: `xray:PutSpans` to `xray:PutSpan` in `modules/compute/main.tf`.
- Unknown service: `logs:PutLogEvents` to `cloudwatchlogs:PutLogEvents`.
- Misspelt key: `token.actions.githubusercontent.com:sub` to `token.actions.githubusercontent.com:subject`.
- Misspelt provider: `token.actions.githubusercontent.com:aud` to `token.actions.githubusercontnet.com:aud`.
- Unreadable action line: one `Action = "kms:Decrypt"` to `Action = "kms:${local.verb}"`.
- Leftover text: one `Action = ["ssm:GetParametersByPath", "ssm:PutParameter"]` to `Action = ["ssm:GetParametersByPath", local.extra]`.
- Data source: add `data "aws_iam_policy_document" "x" {}` to a module.
- File-read policy: one `policy = jsonencode({` replaced by `policy = file("policy.json")`.
- Non-map condition: one `Condition = {` replaced by `Condition = local.conditions`.
- No names: every scanned file emptied in the copy.
- Missing index: the index URL pointed at `https://servicereference.us-east-1.amazonaws.com/v1/no-such-file.json`.
- One service answers 403: the layout for `kms` built as `v1/kms/kmsx.json`.
- Deliberate-break expectation: one in-script copy expected to pass instead.

## Build facts

- Owner: backend (backend-builder).
- Flags: none (CI tooling; no production code, auth or data change).
- Lane: A, after 7.15.
- Mechanisms and sources:
  - AWS's programmatic service reference, fields `Actions[].Name` and `ConditionKeys[].Name`: <https://docs.aws.amazon.com/service-authorization/latest/reference/service-reference.html>.
  - IAM action names and wildcards: <https://docs.aws.amazon.com/IAM/latest/UserGuide/reference_policies_elements_action.html>.
  - Condition key case: <https://docs.aws.amazon.com/IAM/latest/UserGuide/reference_policies_elements_condition.html>.
  - OIDC federation keys, the exception's authority: <https://docs.aws.amazon.com/IAM/latest/UserGuide/reference_policies_iam-condition-keys.html#condition-keys-wif>.
  - Name matching: bash `[[ subject == pattern ]]` under `shopt -s nocasematch extglob` (Bash Reference Manual, *Conditional Constructs* and *Pattern Matching*: <https://www.gnu.org/software/bash/manual/bash.html#Pattern-Matching>). For an action, the pattern is the Terraform action's name and the subject each reference `Name`. For a condition key, the pattern is the reference name with each `${...}` segment replaced by `+([!:])`, and the subject the bounded key.
  - curl options: <https://curl.se/docs/manpage.html>.
  - Step timeout: <https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idstepstimeout-minutes>.
- Why a script, and why read the `.tf` files: a plan JSON (the input Parliament, Conftest or AWS's validators need) needs a real `terraform plan`, which reads data sources and so needs credentials; `terraform test` exports no plan JSON; the test files pin only some policies (`sts:AssumeRole` in `modules/compute` is in none). The policies are short hand-written `jsonencode()` maps, so line rules read every name, and criterion 2 refuses every shape they cannot read. The script follows `infra/deploy/aws/deploy.test.sh`'s style (`set -euo pipefail`, `fail`, `ok`, `section`); the check function has its own failure path.
- Evidence, 2026-11-01 (Sofia, read-only fetches with `curl`, no credentials):
  - Every action in today's tree is in its service's file.
  - `aws:SecureTransport`, `aws:SourceArn` and `aws:SourceAccount` are in the global keys; `kms:ViaService` and `kms:EncryptionContext:${EncryptionContextKey}` are in `kms.json`.
  - `sts.json` lists `token.actions.githubusercontent.com:repository`, `:ref` and others, but not `:aud` or `:sub`.
  - `ssm:SendComand` is absent, and an unknown service's URL answers 403.
  - Every file for today's six services (`s3`, `sts`, `ssm`, `kms`, `xray`, `logs`) has `Actions` and `ConditionKeys` arrays. None of their names, nor the global keys' names, holds `*`, `?`, `[` or another pattern character apart from `${...}` segments, so `[[ == ]]` needs no escaping.
  - Today's tree has no `aws_iam_policy_document`, no heredoc or file-read policy and no non-map `Condition`. Its one `templatefile()` is `user_data` in `modules/compute`.
- Packages: none.
- Touches:
  - `infra/terraform/iam-names.test.sh` (new; criteria 1 to 6)
  - `.github/workflows/ci.yml`: one step in the `terraform` job and the one-line comment above `publish` (criterion 7)

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md, section Requirements
- story — docs/tickets/epic-7-release/tickets/story-7.5-terraform-dev-environment-and-oidc-deploy.md, "How the plans assert"
- story — docs/tickets/epic-7-release/tickets/story-7.15-deploy-role-trusts-the-immutable-oidc-subject.md, Notes (`oidc-subject` does not gate `publish`)
- architecture — docs/architecture/architecture.md, AD-28, AD-32
- prd — docs/prd/prd.md, NFR-INFRA-2, NFR-INFRA-4
- production path — docs/production-path.md, P46, P56
- verification — docs/verification/7.16-challenge.md, 7.16-prebuild-review.md, 7.16-prebuild-security.md
- research — ~/disputedesk-research/research_notes/LocalStack testing/offline-checks.md, Risk-by-tool table (IAM rows) and *Recommended minimal set* item 2 (outside the repo)

## Notes

- Risk low (proposed): the check adds a gate and changes no policy; a wrong check fails CI loudly, and a missed name is what happens today.
- Human decision, 2026-11-01: every IAM `Action` and condition key in the Terraform policies must exist in AWS's programmatic service reference, a public HTTPS JSON fetch with no credentials, run in the existing `terraform` CI job with a timeout on every fetch; obtain the policy names in the simplest way that works and justify any script; include a deliberate-break criterion. No story runs any AWS command or calls AWS.
- Human decision, 2026-11-01 (7.16 challenge): the OIDC exception is exactly `token.actions.githubusercontent.com:aud` and `:sub`, on the IAM User Guide's OIDC federation keys, labelled not verified against the service reference; any other unknown key fails.
- Decision: 2026-11-01 (Sofia, 7.16 write) — names are read from the `.tf` files with line rules, not from `terraform test` (no plan JSON export) or from the test files (a copy of the source, and not every policy is pinned there); see Build facts.
- Decision: 2026-11-01 (Sofia, 7.16 refine) — the line rules are kept, and the claim is narrowed to what they can read: every shape outside them fails the check (criterion 2), with one supervisor break per shape. An HCL parser would be a new tool for six short policies.
- Decision: 2026-11-01 (Sofia, 7.16 write) — names compare case-insensitively, as IAM evaluates them, so a check failure always means a name IAM would not recognise.
- Decision: 2026-11-01 (Sofia, 7.16 refine) — `publish.needs` stays unchanged. Adding `terraform` would tie image publishing to AWS's public endpoint being reachable, and no automated step consumes the Terraform: plans and applies run only from the maintainer's machine from a green `main`. The comment above `publish` ("after every other CI job passes") has been wrong since 7.5 and is corrected once, here, because 7.16 builds after 7.15 (which adds `oidc-subject`, also outside `needs`).
- Decision: 2026-11-01 (Sofia, 7.16 refine) — whole-run bound. One URL's worst case is about 90 s: retries may start until `--retry-max-time 60` has passed, and the last attempt may run its `--max-time 30`. Today's 8 URLs (the index, the global keys and six services) could therefore take about 12 minutes in sequence, more than the job's `timeout-minutes: 10`. The step gets `timeout-minutes: 5` as the whole-run bound. The rest of the job took 33 to 46 s in its six latest green `main` runs (`gh run view`, 2026-11-01), so the job ends within about 6 minutes, under its 10, and the job timeout stays 10.
- Assumption: a quoted map key holding `:` outside a `Condition` (for example a tag key) or a set-operator key such as `"ForAllValues:StringLike"` would be read as a condition key and fail loudly; none exists today, and a failure, not a skipped name, is the safe side.
- Collision: one step in the `terraform` job and the comment above `publish`; 7.15 adds a new job and 7.17 a `platform` step, in other sections of `ci.yml`. 7.16 rebases on 7.15.
