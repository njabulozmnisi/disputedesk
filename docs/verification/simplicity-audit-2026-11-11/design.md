# Simplicity and standards audit, 2026-11-11: design

Set: `docs/architecture/architecture.md` (AD-1 to AD-32, AD-14a, AD-19a), `docs/architecture/spine-amendments.md`,
`docs/decisions/ad-001` to `ad-012` (no ad-004 here; it is in `docs/process/`), `docs/prd/prd.md`,
`docs/initiative-disputedesk.md`, `docs/production-path.md`, `docs/glossary.md`, `docs/conventions/backend.md` and
`frontend.md`. Tree: the lows-2026-11-10 worktree at `88b28c3`, which is ahead of main `f6193f5`. Read-only.

Rules: AGENTS.md "How to work", the conventions, and `review-story` pass 2 with its standards check. The severity scale
and tags are the same as the 2026-11-03 baseline (`~/disputedesk-rename/retro-8/audit-rerun-plan.md`). Line numbers are
from the tree above.

## 1. Findings

| ID | Severity | Tag | File:line | Finding | Simplest fix |
|---|---|---|---|---|---|
| DESI-1 | medium | [decision] | `docs/architecture/architecture.md:416`; `docs/conventions/backend.md:27`; against `architecture.md:486,493` | The migration rule still says that before "the first release" no environment holds the schema except disposable local and CI databases, so the history "may be reshaped in place". That has been false since the AWS environment was applied on 2026-11-07: RDS holds the schema (AD-28:486). AD-28:493 also makes rollback "a dispatch of `deploy.yml` with the previous SHA", which only works if each migration stays compatible with the previous release's code. Today the rule asks for that compatibility only "from the first release". | A human decides whether the 2026-11-07 deploy was the first release. If it was, state expand, migrate, contract from now on ([EF Core, *Managing migrations*](https://learn.microsoft.com/ef/core/managing-schemas/migrations/managing)) in both files. If it was not, say that the AWS RDS is disposable, is recreated whenever the history is reshaped, and that a rollback across a migration recreates it. |
| DESI-2 | medium | [decision] | `docs/production-path.md:241` (P55), `:301` (P71), `:333` (P80), `:337` (P81); against `:139` (P46) | Four accepted risks still give "the environment has never been applied" or "the application is not yet deployed to AWS (P46)" as the reason they can wait. P46 itself says the environment was deployed on 2026-11-07. P80 matters most: the Zitadel bootstrap token lives on an instance volume, and every human administrator is now deactivated, so losing the instance means a fresh Zitadel database. These risks were accepted on a premise that no longer holds. | A human re-accepts each of the four with a reason that is still true (synthetic data, a dedicated account), or schedules the fix. Change each "Why it waits" line to match. |
| DESI-3 | low | [code] | `docs/conventions/backend.md:24`; against `architecture.md:255` | The convention says the publish mapping maps every stored event type "to its integration event or to nothing". AD-14 says it has "no case that publishes nothing": every stored event has been published since `SlaWarningRaised` was dropped (8.14). A builder who follows the convention could add an unpublished type. | Delete "or to nothing". |
| DESI-4 | low | [code] | `docs/decisions/ad-006-actor-agnostic-dispute-actions.md:9,12,25`; `ad-009-stored-events.md:9,13,14,18,28`; `ad-012-github-flow-ci.md:11,13,15,28` | Three decision records still state replaced rules in their Decision text and fix them in notes further down. ad-006 lists `RecordSlaWarning` and the `Operations:Actors` map. ad-009 lists `SlaWarningRaised` and the "version gaps" it caused. ad-012's table still answers "deploy on merge" and `DEPLOY_ENABLED`. ad-012 clause 6 gives `git rebase --onto main <base>`, which differs from AGENTS.md Commits (`--onto origin/main <base's last head>`) and does not work once GitHub has deleted the merged base branch. A reader has to reconcile each body with its notes. | Follow Nygard's form ([*Documenting Architecture Decisions*](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions)), which the spine already cites: rewrite each Decision as it stands now, and keep the replaced clauses as one dated line each under "Superseded". |
| DESI-5 | low | [code] | `architecture.md:148,164,271,430,486,488,539`; `spine-amendments.md:128` | Dated history is creeping back into rule text, against the spine's own rule (`architecture.md:33`: "states its current rule only"). There are ten dated clauses in all: AD-7 (2), AD-8, AD-14a, AD-23, AD-28 (4) and AD-32. AD-28:486 also tells history ("Until the AWS free-plan account existed…"). Two changes have no history row: AD-23's empty-style hash (2026-11-08, `ui-linking`) and AD-28's IAM template check (2026-11-04). The AD-21 row at `spine-amendments.md:128` sits after a bullet list, outside any table, so it does not render as a row. | Move each dated clause into a row in `spine-amendments.md`. Add the AD-23 and AD-28 rows. Move line 128 into the 2026-11-03/04 decisions table. |
| DESI-6 | low | [code] | `architecture.md:539` | The AD-32 Rule runs to about 13 sentences, against the two-to-five-sentence format (`architecture.md:33`). It repeats ad-012 clause by clause: the job list, the `e2e-regression` path scope, the gate jobs, the `floor-guard` check, the composite actions, publish, deploy and the ruleset. | A two-sentence Rule: "Every check job of `ci.yml` and `security.yml` runs on every pull request and main push, and each workflow's gate job (`ci-passed`, `security-passed`) is what the main ruleset requires. Main squash-merges, publishes SHA-tagged images to GHCR, and `deploy.yml` deploys a chosen SHA by digest when dispatched." Move the rest to Details or leave it to ad-012. |
| DESI-7 | low | [code] | `architecture.md:309-323` | AD-17's *Polling* detail carries a design study that nothing requires. It has a four-row table of "ways to send sooner" (`LISTEN`/`NOTIFY`, CDC, a shorter wait, publishing in the request), names `LISTEN`/`NOTIFY` as "the first choice if a requirement ever bounds the delay", and records a one-off measurement ("left after 2.8 seconds"). The text itself says "No requirement bounds the delay". AGENTS.md puts "anything bigger" in `docs/production-path.md`. | Keep the 10-second rule and its three reasons. Move the options table into P19 (relay tuning). |
| DESI-8 | low | [decision] | `docs/initiative-disputedesk.md:69,87,119`; `docs/prd/prd.md:759,776,169`; against `prd.md:454,639,641` and `architecture.md:440` | Requirement text still predates the AWS apply. R38 says "it is applied when the account exists". The Done-when wants the environment "applied, deployed … and torn down when the AWS account exists", and it cannot be torn down while it is in use (NFR-INFRA-5:641). A Notes open question says the account "does not exist yet". PRD §10 has the same stale line. PRD §10:759 keeps "one Zitadel service account per actor type", which predates FR-25's allowlist. FR-2:169 says the correlation and nonce cookies "use the handler defaults", but AD-24:440 takes their names from configuration (`__Host-` in AWS). | A human restates R38 and its Done-when as delivered (applied 2026-11-07; the destroy check is open while the environment is in use), as was done on 2026-11-03. Drop the stale assumption and open question, and align FR-2 with AD-24. |
| DESI-9 | low | [code] | `architecture.md:862,863,864,869,870`; `production-path.md:147` (P48) | The spine's Deferred and Open questions sections hold dead rows. The CloudFront prefix-list check is "once the account exists", which it now does. The Node 26 move says "revisit after 2026-10-28", and P48 repeats that. One Deferred row says it is "Settled in v1". The first open question is marked settled. The second has waited since 2026-10-26 for a human to confirm that ad-009's concurrency rule governs, yet AD-15 and ad-009 already state that rule and PRD §9 says nothing is open. | Delete the settled rows. Give the Node 26 row a current trigger, or drop it. Close line 870, recording the answer in `docs/verification/decisions.md` if a human confirms. |

No high finding. The design invents no protocol. Every mechanism of substance still cites Microsoft Learn, an RFC, OWASP
or a named pattern. The two custom mechanisms that remain have stated reasons: the strict enum converter (AD-5:120,
because `JsonStringEnumConverter` reads names ignoring case) and the Idempotency-Key filter (AD-16, which follows the
IETF draft and has no framework equivalent).

## 2. Counts against the baseline

| Severity | Baseline (2026-11-03) | Now (2026-11-11) |
|---|---|---|
| high | 1 | 0 |
| medium | 10 | 2 |
| low | 6 | 7 |

Fate of the baseline findings, compared by kind because the old lines no longer resolve:

| Baseline | Kind | Fate |
|---|---|---|
| DES-1 | Rules grown by dated amendment; some rested on framework source | Closed by `fed7f85` and the 2026-11-03/04 decisions. AD-7, 8, 12, 13 and 30 now carry 3 dated clauses, against 65 at the baseline. The framework-source claims are gone: AD-8's `AcceptsMetadata`, AD-12's permit count and AD-30's two-instance seeding analysis. A smaller creep elsewhere is DESI-5 |
| DES-2 | Event sourcing beyond any requirement | Accepted (human decision 2026-11-03; ad-009:18 records the trade-off) |
| DES-3 | `SlaWarningRaised` and the warning claim | Closed (8.14). Leftovers are DESI-3 and DESI-4 |
| DES-4 | AD-8's two check orders | Closed (8.10): one order (AD-8:159) |
| DES-5 | AD-9's actor map | Closed (8.12): `Operations:AllowedClients` (AD-9:171). Leftover in ad-006 (DESI-4) |
| DES-6, DES-7 | AD-30 seeding through a second context; the two-instance analysis | Closed (8.10, 8.15; spine-amendments.md:87): one named bypass in `Seeder.cs` |
| DES-8 | AD-7's cross-tab deadline | Closed (8.13): per-tab deadline, `refetchOnWindowFocus`, P76 and P77 |
| DES-9 | Two Data Protection key rings | Accepted (human decision 2026-11-03; AD-7:148) |
| DES-10 | AD-28 hardening on an environment never applied; seven single-use modules | Accepted (human decision 2026-11-03). The "never applied" half is moot since 2026-11-07, which is where DESI-1, 2 and 8 come from |
| DES-11 | Team folders with no requirement behind them | Accepted (ad-011:5) |
| DES-12 | ad-004 in the solution's decision set | Closed: moved to `docs/process/` (architecture.md:816) |
| DES-13 | AD-5's second enum converter | Closed (8.11): one converter factory |
| DES-14 | (CORE-7) | Closed (8.2) |
| DES-15 | AD-29's AWS telemetry gap against SM-1 and SM-2 | Closed by the 2026-11-03 restatement (NFR-OBS-4, §8). R38's matching text is still stale (DESI-8) |
| DES-16, DES-17 | `now()` wording; other docs fixes | Closed (`fed7f85`; AD-4:104,109) |

ad-012 passes the two-sentence test in substance: pull requests carry the checks behind two gate jobs that the main
ruleset requires, main takes squash merges and publishes images by SHA, and each deploy is dispatched by hand. Its body
still carries the replaced clause 4 text (DESI-4).

## 3. Trend

Every baseline medium was closed by a story or accepted by a human decision. What remains is no longer over-built
mechanism: it is documentation that has fallen behind the 2026-11-07 AWS apply and the 2026-11-03 decisions. The two
mediums are the same drift in places where it matters: a migration rule that no longer matches a deployed database, and
accepted risks whose stated reason is now false.
