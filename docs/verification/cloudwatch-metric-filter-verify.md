# Verification: cloudwatch-metric-filter

- Commit: `c808f6b45c290d8fe9e974b0d5fb209ed2019d77`, uncommitted diff (diff hash now `583acbfc767e`; the builder's report says `498492531a20`)
- Verifier: Sonnet 5.5 (procedure), `scripts/verify-with-copilot.sh` (gpt-6-astra), `scripts/run-breaks.sh`, `scripts/floor-guard.sh`
- Date (UTC): 2026-11-09
- Worktree: `/Users/njabulomnisi/Projects/Dojo/DisputeDesk/.claude/worktrees/agent-af0348ebc48d2a3c2`

## Result: pass (low findings only)

## Diff hash

The current hash differs from the report's. The cause is the docs-only edits the supervisor made after the build
(`docs/architecture/architecture.md`, `docs/architecture/spine-amendments.md`, `docs/verification/decisions.md`). I could not
reproduce `498492531a20` by excluding those paths, so the hash is not proof by itself. File times show the code is the code
the suite ran on: `infra/images/otel-collector/config.yaml` (07:38:31) and `ComposeRenderTests.cs` (07:39:11) were last written
before the results file's run began (05:39:24Z = 07:39:24 local), and `infra/terraform/README.md` (07:35:02) too. The three docs files were
written at 07:43, after the run, and are prose only. Finding L1.

## Results file

`TestResults/*.trx`: 440 executed, 440 passed, 0 failed. The three collector tests passed:
`CollectorConfig_ExportsOnlyTheAppsMetricsEachWithItsOwnAttributesAsItsOneDimensionSet`,
`CollectorConfig_ValidatesWithThePinnedCollectorImage`,
`CollectorConfig_HasNoDebugExporterAndLimitsMemoryFirstInEveryPipeline`. The suite was not re-run.

## Criteria against the diff

| Criterion | Mechanism in the diff | Test | Proved |
|---|---|---|---|
| Only `disputedesk.*` metrics exported | `filter/app-metrics` with `not HasPrefix(metric.name, "disputedesk.")`, in the metrics pipeline after `memory_limiter`, before `batch` | the `Exports...` test posts `aspnetcore.routing.match_attempts` and asserts it is absent from the EMF output | yes |
| Nothing else reaches the log group | the filter runs before `awsemf`, so no record is written for a dropped metric (an `awsemf` `metric_declarations` list would still write one; break 3 shows it) | same test, plus the assertion on the dropped name in stdout | yes |
| Own attributes as the one dimension set, no rollups | `dimension_rollup_option: NoDimensionRollup`; the test asserts exactly `disputedesk.dispute_actions: OTelLib,action,actor_type` and `disputedesk.disputes.raised: OTelLib,reason` | same test | yes |
| Config valid, `memory_limiter` first in every pipeline | pipeline strings asserted; `validate` run with the pinned image | the other two collector tests | yes |

Every instrument in `backend/src` (`CreateCounter` and `CreateHistogram`) is named `disputedesk.*`, so the prefix filter drops none of the app's own metrics.
The local Aspire path is untouched: the change is only in the AWS collector config.

## Breaks (`scripts/run-breaks.sh`, throwaway copy, `stack` lock)

```
ok    break 1 (...Exports...): failed as expected   (filter removed from the pipeline)
ok    break 2 (...Exports...): failed as expected   (NoDimensionRollup removed)
ok    break 3 (...Exports...): failed as expected   (metric_declarations instead of the filter)
```

Each break's test passed without the break (the script's baseline check), so each failure is the break's own.

## Floor guard

`scripts/floor-guard.sh` in the worktree: clean, exit 0.

## `scripts/verify-with-copilot.sh`

It ran (gpt-6-astra) and wrote its output to the worktree's `docs/verification/cloudwatch-metric-filter-verify.md`. It did not
reach a usable verdict: this Small-tier fix has no `docs/backlog/story-cloudwatch-metric-filter.md`, so it reported "story file
missing" and `FAIL`, and its sandbox could not run `diff-hash.sh` or `floor-guard.sh`. It found no code defect and confirmed the
criteria-to-test mapping above against the builder's report. I did those two checks myself (above). Its "missing story" point is not
a finding against the change: the criteria are the human's decision of 2026-11-09 (`docs/verification/decisions.md`).

## Non-standard mechanisms

None. Each is sourced in the builder's `standards`: filter processor with OTTL `HasPrefix`, `awsemf` `NoDimensionRollup`, the
recommended processor order, `--set` and `output_destination: stdout` for the test, OTLP/JSON over OTLP/HTTP.

## Findings

- **L1 (low), diff hash:** the report's `498492531a20` no longer matches (`583acbfc767e`) because of the post-build docs edits; the code files predate the test run (see above).
- **L2 (low), AWS effect unverified:** nothing here calls AWS. The effect on the live `DisputeDesk` namespace is known only after the next deploy (the builder says so too). The existing 363 metrics stay until they age out. This is the deploy's check or QA's, not a failure.
- **L3 (low), Linux not run locally:** the new test needs the Docker CLI, which the Linux SDK container lacks. CI's platform job is the Linux run (the builder says so).

No high or medium finding.
