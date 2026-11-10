# readme-landing review

Docs-only change, reviewed as `git diff origin/main` in the builder's worktree (base `6dd7df6`, all changes
uncommitted): `README.md` rewritten (242 to 126 lines), `infra/README.md` (AWS URLs, entry and budget bullets, "What
public traffic costs"), `docs/architecture/architecture.md` (lifecycle diagram into AD-13), `docs/prd/prd.md`
(NFR-SEC-11), `docs/production-path.md` (P50), `docs/verification/decisions.md` (one row). The owner's 2026-11-10
decisions (a short landing README, the humanize rules, publishing the AWS Mailpit login) are taken as given. Publishing
the login is not a finding here. Whether the records describe its effect correctly is in scope.

## How it was checked

- **Requirements:** PRD lines 149, 247, 522, NFR-SEC-11 (594), NFR-DOC-1 (645), NFR-DOC-5 (649-652); initiative R39
  (70, 88); production-path P40 (201); story 7.7 criteria 1 to 3 and Notes 109-117; architecture Deferred (861) and
  ad-005 (23), which also say "the README says to reset".
- **Old content:** `git show origin/main:README.md`, section by section, against `docs/architecture/architecture.md`
  (Stack, Component view, Deployment view, AD-13), `infra/README.md`, `infra/terraform/README.md`, `docs/onboarding.md`,
  `docs/README.md#how-it-was-built` and ad-011.
- **Facts:** `infra/images/gateway/templates-aws/nginx.conf.template`, `infra/deploy/aws/compose.yml`,
  `infra/images/otel-collector/config.yaml`, `infra/terraform/modules/budget/main.tf`, `modules/edge/main.tf`,
  `CustomerLinkConfiguration.cs`, `Dispute.cs` (`IsAllowed`), `scripts/operations-token.sh`, `infra/terraform/README.md`
  (the `MAILPIT_UI_AUTH` recipe) and `docs/verification/7.4-prebuild-security.md`.
- **Links:** every relative link target in `README.md` exists. Every anchor link into a README in the repo was listed,
  and each resolves: the AD-13 and AD-20 GitHub slugs, `infra/README.md:110` `../README.md#on-aws`,
  `infra/README.md:168` `../README.md#try-it` (the brief's line 145 is now 168), `infra/terraform/README.md:13`
  `../README.md#deployed-on-aws` (this is infra/README, which keeps that heading). No test or script reads the root
  README.
- **Rules:** `scripts/secret-scan.sh` exits 0 on the change (gitleaks: no leaks, the published password included). No
  forbidden term in any of the six files. The README and the moved infra text were scanned for the humanize list (AI
  vocabulary, "not just", "ensure", em and en dashes, bold inline headers): no hits. Headings are sentence case. No
  person is named or referred to in the third person.

## Results by check

1. **Lost requirements: none.** Build, run and test (README 13-28, 78-89). Demo logins for A and B, and the IDs and
   accounts for C and D (32-37). The Scalar operations walkthrough (48-56). The OTP-by-SMS note (46-47, NFR-SEC-11).
   Zitadel as the stand-in (8-9, PRD 149). In-memory rate limits, now with "reset on restart and aren't shared" (96-97,
   PRD 247, story 7.7 Note 115). The three NFR-DOC-5 simplifications (93-95). The reset note (27-28, P40,
   architecture 861, ad-005). Fixture logins live on AWS (39, story 7.7 Note 117). The runbook and onboarding are
   linked (108, 110). The README still ends with the ten-area map (113-126).
2. **Nothing is gone from the repo.** The component diagram is in architecture's Component view and infra/README's
   local stack. Tech stack: architecture Stack, plus `frontend/README.md` (shadcn, Orval), ad-012 (CodeQL, Trivy, ZAP),
   ad-009 and ad-010 (CloudEvents), ad-001 and ad-007 (Zitadel v4, server-side sessions). The AWS parts table moved
   like this: entry and budget to infra/README 103-104; no SSH, CloudFront-only, RDS TLS and SSM to infra/README
   57-100; db.t4g.micro and Transaction Search to architecture and the terraform README. The cost section is now
   infra/README 112-125. The lifecycle diagram matches `Dispute.IsAllowed` (`Dispute.cs:258-261`). "Escalated shows as
   Under review (prioritised)" is in AD-13's customer wire view and the glossary. The repo map is onboarding 14-26.
   The one-repo note is ad-011, linked at README 109. How it was built is docs/README 33-110.
3. **Facts.** Correct: the budget thresholds (`budget/main.tf:32-47`); the Free plan wording; only the api and the
   worker get `OTEL_EXPORTER_OTLP_ENDPOINT` on AWS (`aws/compose.yml:24-27`, merged at 114 and 152, with no
   CloudWatch logging driver); plain HTTP redirected or refused (`edge/main.tf:69,83`); the 10 r/s limit keyed on the
   CloudFront viewer address (`nginx.conf.template:60,85`); customer D linkable once (`CustomerLinkConfiguration.cs:19`,
   unique `OneLinkPerCustomer`); password-reset emails reaching Mailpit (Zitadel SMTP is `mailpit:1025`,
   `aws/compose.yml`); the Mailpit user `mailpit` with a 64-character `[A-Za-z0-9]` password (`terraform/README.md:179`).
   See M1, L1, L2 and L3 for what is wrong.
4. **Links:** all resolve (above).
5. **Humanize, neutral voice, glossary:** no breaches found. Suggestion 2 below is optional.
6. **Concision:** 126 lines, and each section answers a first-time reader's question. Suggestions below.

## Findings

### High

- **H1. P50 and the decision row leave out the largest effect of publishing the Mailpit login: taking over Zitadel's
  bootstrap administrator.** `docs/production-path.md:221`, `docs/verification/decisions.md:24`.
  - P50 says a reader can "reset and take over the fixture logins". On AWS the first-instance human admin, `admin`
    with `admin@disputedesk.test`, verified (`infra/deploy/aws/compose.yml`, the `ZITADEL_FIRSTINSTANCE_ORG_HUMAN_*`
    lines), is also a Zitadel login whose reset email goes to the same public Mailpit.
  - It holds IAM_OWNER, signs in with a password only, and has no MFA (7.4 pre-build security S7.4-3; the login
    policy leaves MFA optional, `docker-compose.yml` comment above `LOGINV2_REQUIRED`).
  - The AWS auth host blocks only `/ui/console` (`nginx.conf.template:213`). `/ui/login`, `/oauth/v2/*` and
    Zitadel's admin and management APIs are still proxied (`:218-221`).
  - Until now the 64-character Mailpit secret stood between a reader and that reset email. With it published, any
    reader can likely reset the instance owner's password and sign in. From there, a token for the APIs through the
    console's public PKCE client looks reachable, which would let a reader change the DisputeDesk app's redirect URIs,
    the SMTP settings or the users.
  - Not exercised: nothing was run against AWS. Two points are unverified: that `admin` is accepted as a login name
    on AWS, and the token step.
  - The human accepted "synthetic-demo exposure" for fixture logins and the inbox, not control of the identity
    provider. P50's "*Why it waits:* ... a fixture login reaches no real customer" doesn't cover this case.
  - Fix (needs a human decision under Human gates, high-severity security):
    - Either accept it in writing. Add the bootstrap admin takeover to P50 and to the decisions.md answer.
    - Or close it in a separate infra change, then say so in P50. Options: deactivate or lock the human admin on AWS
      (the provisioner uses the machine PAT, not that login), hide password reset in the instance login policy, or
      require a second factor for it.

### Medium

- **M1. The AWS table tells readers to use an operations token "as in step 6", which no reader can get for AWS.**
  `README.md:69`. `scripts/operations-token.sh:11,15` reads the operations client secret from the local `zitadel`
  container and posts to `http://auth.localhost:8080`. Its token comes from the local issuer, which the AWS Api
  rejects. The AWS client secret is only on the instance. The old README listed AWS Scalar with no sign-in claim.
  Fix: in the "Sign in with" cell say the operations calls work on the local stack only, or leave the cell empty.
  Getting an AWS token would need SSM access to the instance.

### Low

- **L1. The traffic-cost bullet doesn't match the diagram above it, and doesn't fit the worker.**
  `infra/README.md:120-121`.
  - It says the api and the worker send "traces, logs and metrics to CloudWatch for every request they handle". The
    diagram (`:78-79`) and AD-29 send traces to X-Ray (`otel-collector/config.yaml:44-45,71`). With Transaction
    Search, X-Ray stores and bills those spans in CloudWatch Logs.
  - The worker handles no requests. It relays the outbox.
  - Fix: something like "the api traces and logs every request, and the collector sends the api's and the worker's
    spans to X-Ray (stored in CloudWatch Logs) and their logs and metrics to CloudWatch".
- **L2. The rate-limit scope leaves out one of the limited routes.** `infra/README.md:124` says "on `/api/`,
  `/scalar` and the Mailpit host". `= /openapi/v1.json` is limited too (`nginx.conf.template:179-181`). Fix: add
  `/openapi/v1.json`, or say "on the Api, Scalar and Mailpit routes".
- **L3. "The same logins work on AWS" can stop being true.** `README.md:39`, with the warning at `:72-73`. P50 says a
  reader can reset and take over the fixture logins A, B and C. The README warns only that D links once. Fix: at
  72-73, add that anyone can reset the shared logins, so they may stop working.

### Pre-existing, not from this diff

- `docs/production-path.md:261` says "the README says plainly what a compromise of the Api or the collector exposes".
  Neither the old nor the new README says this. For the next lows batch.

## Optional readability suggestions (not findings)

1. `README.md:87-89`: the regression-suite sentence (`regression-paths.list`) is contributor detail. The e2e README
   or `ci.yml` could hold it, leaving "CI runs every check on every pull request".
2. `README.md:63`: "Its account is on the AWS Free plan". The glossary's Account is a bank account, so "Its AWS
   account" reads more clearly.
3. `README.md:70`: the 64-character password makes the AWS table very wide. A line under the table ("Mailpit login:
   user `mailpit`, password `…`") keeps the table narrow.

## Standards check

No mechanism added (docs only). The facts cited match their documented sources: AWS Budgets notifications
(`budget/main.tf`), the AWS Free plan page linked at `infra/README.md:115`, nginx `limit_req` with `realip`, and
Mailpit's `MP_UI_AUTH` basic auth.

## Verdict

`changes`. H1 needs the human's decision before the fix round. M1 is a one-cell fix. L1 to L3 go to
`docs/verification/lows.md` if they are not fixed in the same round.
