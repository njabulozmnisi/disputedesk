# cloudwatch-metric-filter: post-build review

- Date: 2026-11-09
- Tier: Small (infra config fix, below High-stakes, so the security checklist runs here)
- Worktree: `.claude/worktrees/agent-af0348ebc48d2a3c2`, uncommitted diff against `main` (`HEAD` = `main` = `c808f6b`)
- Builder report: `docs/verification/cloudwatch-metric-filter-build.json` (worktree)
- Verdict: **approve**. No high or medium findings; three lows.

## Scope read

`infra/images/otel-collector/config.yaml`, `infra/tests/DisputeDesk.PlatformTests/ComposeRenderTests.cs`,
`infra/terraform/README.md`, `docs/architecture/architecture.md` (AD-29 rule), `docs/architecture/spine-amendments.md`
(Decisions of 2026-11-09), `docs/verification/decisions.md` (new row). Context read: `Telemetry.cs`, every
`CreateCounter`/`CreateHistogram` in `backend/src`, AD-20, `infra/deploy/aws/compose.yml`, the CI `platform` job,
`Processes.cs`. Pinned upstream read at tag `v0.164.0`: `processor/filterprocessor/README.md` and `config.go`,
`exporter/awsemfexporter/README.md`, `metric_translator.go`.

## Pass 1: correctness

**The filter.** `filter/app-metrics` with `metric_conditions: [not HasPrefix(metric.name, "disputedesk.")]` is the
current, non-deprecated form in v0.164.0 (`config.go` line 53 `MetricConditions ... mapstructure:"metric_conditions"`;
README "Basic Config": a metric is dropped when any condition is true). `error_mode: ignore` is the documented default
and recommended mode. Placement `memory_limiter, filter, batch` follows the collector's recommended order. The
`validate` test and the runtime test both run it on the pinned image.

**Why a filter, not `metric_declarations`.** Confirmed in `metric_translator.go` (`translateCWMetricToEMF`): a metric
with no matching declaration still becomes a log event, only without the `_aws` directive. So `metric_declarations`
alone would have kept ingesting the dropped metrics into the log group; the filter is needed for the "no log ingestion"
part of the intent. Break 3 shows it at runtime.

**No rollups.** `dimension_rollup_option: NoDimensionRollup` is a documented value (awsemf README; the default is
`ZeroAndSingleDimensionRollup`). Resource attributes stay out of dimensions (`resource_to_telemetry_conversion` off by
default), so `service.name` does not split the Api's and the Worker's `disputedesk.dispute_actions` series.

**Every `disputedesk.*` instrument still exports.** The code has the eight AD-20 instruments, all on the `DisputeDesk`
Meter, all named with the `disputedesk.` prefix: `disputes.raised` (`reason`), `raise.rejections` (`status`),
`dispute_actions` (`action`, `actor_type`; Api and Worker), `disputes.resolved` (`outcome`, `sla_escalated`),
`disputes.withdraw_age` (histogram, no tags), `linking.events` (`event`), `sla.escalations`, `outbox.failed`. The
filter is name-only, so the histogram passes as the counters do. Every label is a small fixed set, so with no rollup
each metric's CloudWatch count is the product of its label values plus nothing. The dropped meters are `System.Runtime`,
HttpClient and Npgsql (`Telemetry.cs` lines 47-50) and any ASP.NET Core meters. `OTelLib` (the scope name, one value)
is added by awsemf as a label; it adds no series. That matches the cost goal; the docs' "its own attributes" is
slightly loose about it (the config comment at line 57 states it correctly).

**The new test** (`CollectorConfig_ExportsOnlyTheAppsMetricsEachWithItsOwnAttributesAsItsOneDimensionSet`):

- Proves the three criteria: an exact-equality assertion on the sorted `metric: dimension set` list (two app metrics,
  one set each, no `service.name`) gives a positive control and catches both rollups and leaked metrics; the absence
  assertion checks the raw stdout for the ASP.NET Core metric name, which only an exported record could echo (the
  pitfall on absence assertions is respected). The `--set` override changes only `output_destination`, so the pipeline
  under test is the shipped one.
- Breaks: all three fail as expected (`TestResults/run-breaks.txt`): filter removed, rollup option removed, and the
  `metric_declarations`-only alternative.
- Cumulative baseline: two exports (1 then 2) because awsemf keeps a cumulative series' first point as its baseline
  (README `retain_initial_value_of_delta_metric`, default false). Correct.
- Reliability: the host port is random on `127.0.0.1` (`-p 127.0.0.1::4318`, read back with `docker port`), the
  container name carries the process ID, and `finally` removes it; no lock is needed (no fixed port, no Compose stack).
  `docker stop` (SIGTERM, 10 s grace) shuts the receivers first and flushes the batch processor into the exporter, so
  the log read after it is complete; it waits on a log line, not a sleep. See L1 for the one timeout gap.
- Changed assertions in `CollectorConfig_HasNoDebugExporterAndLimitsMemoryFirstInEveryPipeline` keep `memory_limiter`
  first in all three pipelines and pin the metrics pipeline's exact line. Not loosened.

**Test results.** `TestResults/platform-tests.log`: 440 of 440 passed. `scripts/diff-hash.sh` on the worktree today
gives `583acbfc767e`, not the reported `498492531a20`; excluding `docs/architecture/` and
`docs/verification/decisions.md` reproduces `498492531a20` exactly. Those three doc edits were applied after the run
(the builder's `conflicts` lists the guard blocking `docs/architecture/`); no test or check reads them, so the run still
covers every executable change. Recorded for the verifier, not a finding.

AD-3, ownership, event, `Location` and timestamp items of Pass 1 do not apply (no backend code).

## Pass 2: simplicity

The design is the smallest one that meets the intent: one processor with one condition, one exporter option, one line
in the pipeline. The rejected alternative (`metric_declarations`) is both larger and wrong for the log-ingestion part.
The test is long but plain: start, wait, post twice, stop, read, assert. `WaitForCollectorLogAsync` and
`CollectorTestMetrics` each have one caller and are short. No speculative options. Explainable in one sentence: "the
collector drops every metric not named `disputedesk.*` and writes each remaining one with its own labels only."

## Standards check

| Mechanism | Standard | Status |
|---|---|---|
| Drop non-app metrics | Collector `filter` processor, OTTL `HasPrefix` (opentelemetry-collector-contrib v0.164.0, `processor/filterprocessor/README.md`, `pkg/ottl/ottlfuncs/README.md`) | documented form |
| One dimension set per metric | awsemf `dimension_rollup_option: NoDimensionRollup` (`exporter/awsemfexporter/README.md`, v0.164.0) | documented form |
| Processor order | collector `processor/README.md`, "Recommended processors" | documented |
| Test override | collector `--set` (opentelemetry.io/docs/collector/configuration, "Override settings"), awsemf `output_destination: stdout` | documented |
| Test input | OTLP/HTTP with JSON Protobuf encoding (opentelemetry.io/docs/specs/otlp) | spec |

No home-grown mechanism.

## Security checklist (below High-stakes)

- AuthN/Z, browser session, linking, audit: not touched.
- Data: no label carries personal data (AD-20 labels unchanged); the change only removes exported data.
- Supply chain: no new package or image; the collector image stays pinned by tag and digest. `scripts/secret-scan.sh`
  and `scripts/security-local.sh` (Trivy fs and config) reported clean in the builder's run.
- Test: placeholder AWS keys keep the SigV4 extensions off instance metadata; the port binds to `127.0.0.1` only.
- Headers: unchanged.

No security finding.

## Doc edits

- AD-29 rule (`architecture.md` line 484): accurate, references AD-20.
- `spine-amendments.md`: new "Decisions of 2026-11-09" table in the same format as 2026-11-08. Accurate.
- `decisions.md`: row in the table's format (feature, point, answer, date). Accurate.
- `infra/terraform/README.md`: see L2.

## Findings

| # | Severity | File:line | Finding | Fix |
|---|---|---|---|---|
| L1 | low | `infra/tests/DisputeDesk.PlatformTests/ComposeRenderTests.cs:528` | `docker run -d` uses the 60 s `CommandTimeout`, while the `validate` test beside it allows 5 minutes because the first `docker run` of the image pulls it. xUnit v3 orders a class's tests by ID, not source order, so on a cold CI runner this test may be the one that pulls; a slow pull fails it as a timeout. The results of `docker port` (538) and `docker stop` (550) are also unchecked, so a failure there surfaces as a confusing parse error or empty list. | Give the `run` step `TimeSpan.FromMinutes(5)` like `validate`; assert the exit codes of `port` and `stop`. |
| L2 | low | `infra/terraform/README.md:307` | The post-deploy check now says the metrics are only the `disputedesk.*` ones, but `aws cloudwatch list-metrics` keeps listing the 363 old metrics for about two weeks after their last data point, so right after this deploy the command shows them and the check cannot tell a working filter from a broken one. | Use `aws cloudwatch list-metrics --namespace DisputeDesk --recently-active PT3H ...` (ListMetrics `RecentlyActive`, the API's one allowed value), or say the old names stay listed for two weeks. |
| L3 | low | `infra/images/otel-collector/config.yaml:34-36` | The comment cites "filter processor README, metric_conditions" for the claim that awsemf writes an unmatched metric to the log group; that behaviour is in awsemf's `metric_translator.go` (`translateCWMetricToEMF`), and no README states it. | Cite awsemf `metric_translator.go` (v0.164.0) for that clause; keep the filter README for `metric_conditions`. |

No high or medium findings: no fix round.

## Capture (after the merges of #33 and #35)

### Escapes

| Finding | Caught by | Should have been caught by | Category | Countermeasure |
|---|---|---|---|---|
| The AWS collector exported every runtime and framework metric with rolled-up dimensions (363 custom metrics); nothing checked metric volume before the AWS environment went live | an AWS Free Tier usage email, 2026-11-09 | the AWS environment's story (its review of AD-29) | non-standard mechanism | fixed by `cloudwatch-metric-filter`: a collector test asserts the exact metrics and dimension sets exported |
| The deploy of 575bd39 left the otel-collector on its old config: `docker compose up -d` does not recreate a container when only a bind-mounted file changes | the supervisor's post-deploy check of the metrics stream | the AWS deploy story's review of `deploy.sh` | environment or tooling | fixed by `deploy-recreate-on-config`: `--force-recreate`, with a DeployScriptTests case; a post-deploy check of what a change was meant to alter stays in the supervisor's deploy steps |

### Metrics

| Fix rounds | Full-suite runs | Stages | Notes |
|---|---|---|---|
| 0 | 1 (platform, 440 tests) | builder; reviewer ∥ verifier; manual collector restart over SSM after the deploy | `cloudwatch-metric-filter` merged as `575bd39` (#33), deployed 2026-11-09 08:41 UTC, effective after the restart |
| 0 | 1 (platform, 442 tests) | builder; reviewer ∥ verifier | `deploy-recreate-on-config` merged as `709f641` (#35); PR needed `gh pr update-branch` to start its checks |
