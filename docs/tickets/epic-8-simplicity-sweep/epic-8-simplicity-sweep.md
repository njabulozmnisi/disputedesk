---
id: 8
type: epic
title: "Simplicity sweep"
parent: initiative-disputedesk
covers: [R2, R3, R9, R10, R13, R16, R17, R19, R20, R21, R22, R23, R24, R25, R27, R29, R30, R32, R33, R34, R35, R37, R38, R39, R40]
sources: [docs/verification/simplicity-audit/mitigation-plan.md]
companions: [docs/verification/simplicity-audit/README.md]
after: []
risk: high
refined: true
---

# Simplicity sweep

## Description

Resolves the code findings of the simplicity and standards audit of 2026-11-03 (`docs/verification/simplicity-audit/`), waves 2 and 3 of its mitigation plan. Wave 2 (stories 8.1 to 8.9) changes how the code reaches the same behaviour: framework helpers in place of copies, standard test tools, one infra test style, linters in CI. Wave 3 (stories 8.10 to 8.17) carries out the human's decisions D1 to D6, D9 and D14b of 2026-11-03, which change what a caller or an operator sees. Wave 1 (docs, S0a and S0b) and wave 4's skill lines are not stories; S15 is dropped (D7: keep SSM in AWS).

## Outcome

A new engineer can explain each mechanism the audit flagged in one or two sentences, and every one follows a cited framework or industry standard; signal: each finding ID in waves 2 and 3 maps to a merged story criterion, and CI is green.

## Requirements

R2 (parent R2): sessions through the BFF, ending after 15 minutes idle. This epic delivers: every session ends in the browser by a document navigation (D2, story 8.13).
R3 (parent R3): sign-out ends both sessions. This epic delivers: sign-out in the SPA is the form post alone, with no in-place clean-up (8.13).
R9 (parent R9): one authorisation state per endpoint. This epic delivers: the SPA's session-state guards in `beforeLoad` (8.3).
R10 (parent R10): refusals are audited. This epic delivers: a refused operations caller's `Denied` entry with its `sub` (8.12).
R13 (parent R13): raising a dispute. This epic delivers: the raise reason read only from its exact camelCase name (8.11).
R16 (parent R16): the dispute timeline from stored events. This epic delivers: the operations timeline without `SlaWarningRaised` (8.14).
R17 (parent R17): disputes scoped to their customer through the ownership filter. This epic delivers: the seeder as the third named bypass (D6, 8.15).
R19 (parent R19): withdrawing a dispute. This epic delivers: the withdraw note validated before the lookup (8.10) and the resolved-409 cell on the stack (8.6).
R20 (parent R20): operations actions and the order of checks. This epic delivers: the framework's order 401, 403, 400, 404, 409, 422 on every route (D1, 8.10).
R21 (parent R21): operations callers. This epic delivers: the `Operations:AllowedClients` allowlist (D4, 8.12).
R22 (parent R22): the SLA sweep. This epic delivers: the sweep escalates only; `SlaWarningRaised` goes (D3, 8.14).
R23 (parent R23): published events. This epic delivers: every stored event published, so `disputeVersion` has no gaps (8.14).
R24 (parent R24): the seed. This epic delivers: the seeder's reads through the named bypass (8.15).
R25 (parent R25): architecture tests. This epic delivers: source rules on Roslyn syntax trees and BannedApiAnalyzers (8.5), and Contracts referencing Domain (D14b, 8.17).
R27 (parent R27): health and heartbeats. This epic delivers: the Worker fails at start without its connection string instead of a green heartbeat (8.2).
R29 (parent R29): PostgreSQL records. This epic delivers: the `SlaWarningRaisedAtUtc` column and its index dropped (8.14).
R30 (parent R30): the API contract and `openapi.json`. This epic delivers: one enum rule, exact camelCase names (D5, 8.11).
R32 (parent R32): the gateway's headers. This epic delivers: the forwarded-header block once per `server` (8.9), proved by one header test for both gateways (8.7).
R33 (parent R33): CI runs every suite and scan. This epic delivers: actionlint and shellcheck in CI (8.1); the two OIDC jobs replaced by a recorded check (D9, 8.16).
R34 (parent R34): code quality rules. This epic delivers: the backend and frontend tidies (8.2, 8.3).
R35 (parent R35): automated tests. This epic delivers: standard test tools (8.4), e2e waits on signals (8.6), one infra test style (8.7, 8.8).
R37 (parent R37): the zero-click stack. This epic delivers: Compose checks on the parsed model (8.7).
R38 (parent R38): Terraform for the dev environment. This epic delivers: IAM policies as `templatefile()` JSON checked by `System.Text.Json` in the infra tests (D9, as answered by the human on 2026-11-04; 8.16).
R39 (parent R39): the README and records. This epic delivers: the infra READMEs name the new checks and the recorded OIDC subject check (8.16).
R40 (parent R40): WCAG 2.2 AA. This epic delivers: the reason radio group as a fieldset with a legend (8.3) and each tab's own inactivity warning (8.13).

## Done when

- Every story of the epic is merged with CI and Security green.
- Each finding ID listed in the stories is either fixed by a criterion or recorded in the mitigation plan's "Kept as is".
- The OpenAPI drift check shows only the contract changes D1, D3 and D5 cause (8.10, 8.11, 8.14); 8.17 shows none.

## Boundaries

- Wave 1 (S0a, S0b: architecture, PRD, decision records, production path) is a docs change outside this epic. Where a wave-3 story needs a requirement's text to match its decision, the supervisor lands that text as a docs-only change before the build (each story's Build facts names the lines).
- No new behaviour beyond the decisions D1 to D6, D9 and D14b. S15 (one key ring) is dropped by D7; the seven Terraform modules stay (D8).
- At most one migration in the epic (8.14).

## References

- parent — docs/initiative-disputedesk.md, section Requirements
- plan — docs/verification/simplicity-audit/mitigation-plan.md, sections 1 and 2
- audit — docs/verification/simplicity-audit/README.md, api-host.md, core-layers.md, design.md, frontend.md, infra-ci-scripts.md, tests.md
- architecture — docs/architecture/architecture.md, AD-1, AD-5, AD-7, AD-8, AD-9, AD-11, AD-13, AD-14a, AD-18, AD-19, AD-21, AD-26, AD-28, AD-30
- prd — docs/prd/prd.md, FR-24, FR-25, FR-26

## Notes

- Decision: 2026-11-04 (Sofia) — the audit ran at `9836438`; every file and line in the stories was re-located against `main` at `40d0a22`. Since the audit, `fixtures/` moved to `backend/fixtures/`, `docker-compose.aws.yml` is `infra/aws/compose.yml`, `infra/nginx/Dockerfile` takes the built SPA through a named build context, and the infra platform checks moved to `infra/tests/DisputeDesk.PlatformTests`.
- Decision: 2026-11-04 (Sofia) — build order follows the plan. Wave 2: S7a linters first (8.1), then S1 (8.2), S2 (8.3), S3 (8.4), S4 (8.5), S5 (8.6), S6 split in two (8.7, 8.8), and S7b nginx last (8.9), so the nginx change is proven by 8.7's one header test. Wave 3, one at a time: S9 (8.10), S13 (8.11), S12 (8.12), S10 (8.13, frontend only, may build beside 8.12), S11 (8.14), S14 (8.15), S16 (8.16), S17 (8.17).
- Decision: 2026-11-04 (Sofia) — tracer bullet: 8.1, CI's linters, because every later infra and script story is checked by them.
- Decision: 2026-11-04 (Sofia) — S7 is split into 8.1 (linters and scripts) and 8.9 (nginx header block), as the plan's order requires.
- Decision: 2026-11-04 (Sofia) — S6 (effort L) is split into 8.7 (the gateway and Compose checks, one header test for both gateways) and 8.8 (the deploy, database-init and Compose-render harnesses), each one session. `infra/terraform/iam-names.test.sh` is not moved by 8.8: 8.16 replaces it (D9) and writes its successor in the infra test project, so it is rewritten once, not twice.
- Decision: 2026-11-04 (Sofia) — S4 builds after S3 (8.5 after 8.4), so the BannedApiAnalyzers rule that bans `MeterListener` and hand-written `ILoggerProvider`s in the test projects (wave 4, item 1) lands in 8.5 once 8.4 has removed them, with one package pin.
- Decision: 2026-11-04 (Sofia, pre-build fixes) — the `MeterListener` ban is dropped from 8.5: 8.4 keeps `StoredEventPrivacyTests`' recorder, scoped to one Api's meter factory, because it must see every instrument including later ones; 8.5 bans hand-written `ILoggerProvider`s and both pooled `DbContext` registrations. 8.2 is High-stakes (linking, money). 8.7 keeps the root-file Compose cases in backend (ad-011 change 4) with a pinned Compose in the `backend` job.
- Decision: 2026-11-04 (Sofia) — TST-10's e2e comment fixes and the resolved-409 withdraw case go to 8.6 (QA owns `e2e/`); its backend parts (`ForbidProbe`, the stale test comments) go to 8.4.
- Decision: 2026-11-04 (Sofia) — the wave-4 deterministic checks come with their slices: BannedApiAnalyzers (8.5), `eslint-plugin-playwright`'s `no-wait-for-timeout` in `e2e/` (8.6), actionlint and shellcheck (8.1), and a test that infra holds no `*.test.sh` (8.8, with `iam-names.test.sh` its one named exception until 8.16 removes it).
- Decision: 2026-11-04 (Sofia) — the human's decisions D1 to D6, D9 and D14b (mitigation-plan.md section 1, 2026-11-03) are carried into stories 8.10 to 8.17 as decided and are not reopened.
- Decision: 2026-11-04 (Sofia) — where a story changes a line in `e2e/`, that line is QA's in the story's QA stage (the guard gives `e2e/` to QA only); where it changes the generated client, the builder regenerates `frontend/src/api/generated/` as backend stories have since 6.1.
- Waits on 4 because: 5 adds the `MeterListener` ban once 4 has removed every use. Waits on 1 because: 7 and 8 land after the linters. Waits on 7 because: 8 shares 7's process helpers, and 9 is proven by 7's header test.
- Waits on 2 and 4 because: 10 changes the withdraw and operations handlers 2 tidies and the tests 4 moves to `FakeTimeProvider` and `DisputeIn`. Waits on 10 because: 11 changes the same request types. Waits on 11 because: 12 follows it in the one-at-a-time order. Waits on 3 because: 13 changes the router and mutator 3 reshapes. Waits on 12 because: 14 follows it, the epic's one migration. Waits on 5 and 14 because: 15 changes the ownership rule 5 rewrites. Waits on 8 because: 16 writes its check in the infra test style 8 sets. Waits on 11 because: 17 changes the enums 11's converter reads.
