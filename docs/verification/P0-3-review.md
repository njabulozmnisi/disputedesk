# Review: P0-3

- Reviewer: `reviewer` agent (Claude Opus 5.5), read-only, two passes
- First verdict: changes (simplicity only; correctness clean, mutation-tested). After fixes: verified PASS (third verifier run, diff hash `900ce07f0a5b`)

| # | Severity | Finding | Resolution |
|---|---|---|---|
| 1 | Medium | `OpenTelemetryErrorListener` redundant with the registration check; process-wide, so a flake risk | Deleted |
| 2 | Medium | `FakeOtlpCollector` tested the SDK's wire format; its free-port helper raced | Deleted; wire export shown in the developer smoke check |
| 3 | Medium | `OtlpExportTests` was 90 lines for one on/off decision | One theory per host (Api and Worker), which also closed the verifier's AC1 gap |
| 4 | Low | A signal-specific OTLP endpoint alone doesn't enable export | Documented in `Telemetry.cs` (same as Aspire) |
| 5 | Low | `A_request_produces_a_server_span` duplicated the 404 traceId test | Deleted; its assertions moved into the 404 test |
| 6 | Nit | `System.Runtime` meter beyond the criteria | Kept (built in, one line), listed as an assumption |
| 7 | — | EF Core command logs at Information duplicated the Npgsql spans | Set to Warning; `DatabaseLoggingTests` proves it and that sensitive-data logging is off |
| 8 | — | Should `/health/*` be excluded from tracing? | Deferred to the tracer-bullet story (the 500-path tests use `/health/live`) |

Known gap: the Worker sends no traces until it does traced work (the outbox relay story re-runs the smoke check).
