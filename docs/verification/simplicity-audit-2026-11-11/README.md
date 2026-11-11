# Simplicity and standards audit, 2026-11-11

The re-run for the `lows-2026-11-10` retrospective (AGENTS.md, Continuous improvement). Six read-only reviewers, one
per file set, with the baseline's rules and severity scale. The baseline is the 2026-11-03 audit (base `9836438`); its
folder left the repository in the rename sweep, and its counts and method survive in the epic 8 retrospective's
re-run plan. Its `file:line` references no longer resolve, so the sets are compared by count and kind, not by line.

Read: the batch branch (main at `f6193f5` plus the lows batch's lane 1 and docs) for every set but the frontend, which
was read on lane 2's fix-round tree.

| Set | Report | Baseline (high / medium / low) | Now |
|---|---|---|---|
| Design | [design.md](design.md) | 1 / 10 / 6 | 0 / 2 / 7 |
| Api host, Contracts, Worker, stubs, build files | [api-host.md](api-host.md) | 0 / 2 / 4 | 0 / 1 / 6 |
| Domain, Application, Infrastructure | [core-layers.md](core-layers.md) | 0 / 1 / 8 | 0 / 0 / 12 |
| Frontend | [frontend.md](frontend.md) | 1 / 2 / 8 | 0 / 0 / 8 |
| Infra, Compose, CI, scripts | [infra-ci-scripts.md](infra-ci-scripts.md) | 1 / 8 / 5 | 1 / 3 / 4 |
| Tests | [tests.md](tests.md) | 2 / 5 / 4 | 1 / 3 / 4 |
| **Total** | | **5 / 28 / 35** | **2 / 9 / 41** |

## Trend

High and medium findings fell from 33 to 11: the simplicity sweep closed most of the home-grown mechanisms and the
abstractions that did not earn their place. Lows rose from 35 to 41, mostly small duplication (a query copied five
times, one error sentence written nine ways) and stale wording.

What is left at high and medium sits in tests and tooling:

- INFR-1 (high): the shell test harnesses removed from `infra/` grew back in `scripts/` (10 `*.test.sh`, 1,588 lines,
  copied helpers), and the check that stopped them scans only `infra/`.
- TEST-1 (high): hand-written e2e poll loops and sleeps get past the `waitForTimeout` lint rule instead of using
  Playwright's `expect.poll` or `toPass`, and two of them reach their deadline without failing.
- INFR-2, TEST-3, TEST-4 (medium): workflow steps tested by copying their `run:` text out of the YAML; platform tests
  driving containers through the docker CLI with their own wait loops where Testcontainers already exists;
  `provision.sh` tested by slicing its text.
- DESI-1, DESI-2 (medium, `[decision]`): AD-22 still lets migrations be reshaped in place, and four accepted risks
  wait on "not yet deployed", both false since the AWS environment went live on 2026-11-07.
- INFR-3 (medium, `[decision]`): hand-written IAM JSON templates make 18 variables repeat a validation that
  `jsonencode` would make unnecessary.
- API--1 (medium): `AddLinkingEndpoints` only forwards to `AddLinkingRateLimits`.

The `[decision]` findings go to the human with the retrospective; the rest become feature specs or lows, as the
retrospective proposes.
