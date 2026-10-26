---
id: 4
type: story
title: "AWS Compose override and OpenTelemetry collector"
parent: epic-release
covers: [R32, R36, R38]
after: [3, 5.1]
hitl: false
risk: medium
---

# AWS Compose override and OpenTelemetry collector

## Description

Adds `docker-compose.aws.yml` with required variables and memory limits, the collector replacing the Aspire Dashboard, and Mailpit basic auth, all testable without an AWS account.

## Requirements

Verify: Reading `docker-compose.aws.yml`, the collector config and the AWS nginx template shows the criteria below.

1. **No inline defaults** (R32; NFR-SEC-9)
   **Given** the override
   **When** it is read
   **Then** every secret and environment value uses `${VAR:?}` with no inline default

2. **Collector in place of the dashboard** (R36; NFR-OBS-4)
   **Given** the override
   **When** it renders
   **Then** a pinned-by-digest `otel/opentelemetry-collector-contrib` with `sigv4auth` receives OTLP and exports traces to X-Ray, metrics to CloudWatch and logs to a log group

3. **Mailpit and origin protection** (R32, R38; NFR-SEC-11, NFR-INFRA-3)
   **Given** the AWS nginx template
   **When** it renders
   **Then** Mailpit's UI needs basic auth and requests without the origin-verify header are rejected

4. **Fits the instance** (R38; NFR-INFRA-3)
   **Given** the override's memory limits
   **When** they are summed
   **Then** they total about 3.4 GiB with Redpanda at 512M

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- `docker compose -f docker-compose.yml -f docker-compose.aws.yml config` fails without the variables and validates with them.

## Build facts

- Owner: backend (backend-builder).
- Flags: none.
- Lane: B.
- Touches: `docker-compose.aws.yml`; `infra/otel-collector/config.yaml`; infra/nginx/templates/ (Mailpit basic auth, origin header check).

## References

- parent — docs/tickets/epic-release/epic-release.md

## Notes

- Gold-plating watch: no extra services beyond the collector.
