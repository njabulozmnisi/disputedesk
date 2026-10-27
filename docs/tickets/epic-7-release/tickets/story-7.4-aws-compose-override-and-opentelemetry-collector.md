---
id: 4
type: story
title: "AWS Compose override and OpenTelemetry collector"
parent: epic-7-release
covers: [R32, R36, R38]
after: [3, 5.1]
hitl: false
risk: medium
---

# AWS Compose override and OpenTelemetry collector

## Description

Adds `docker-compose.aws.yml` with required variables and memory limits, the collector replacing the Aspire Dashboard, and Mailpit basic auth, all testable without an AWS account.

## Requirements

Verify: Reading `docker-compose.aws.yml`, the collector config, the AWS nginx template and the Api's Data Protection registration shows the criteria below.

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
   **Then** Mailpit's UI needs basic auth and requests without the CloudFront origin-verify custom header are rejected

4. **Fits the instance** (R38; NFR-INFRA-3)
   **Given** the override's memory limits
   **When** they are summed
   **Then** they total about 3.4 GiB with Redpanda at 512M

5. **Data Protection keys encrypted at rest in AWS** (R32, R2; NFR-SEC-9, NFR-SEC-1)
   **Given** the Api under the AWS override
   **When** it starts
   **Then** the Data Protection key ring is stored with AWS's documented `Amazon.AspNetCore.DataProtection.SSM` package (`PersistKeysToAWSSystemsManager`) as SSM Parameter Store SecureStrings encrypted with KMS, chosen by configuration, while local development keeps the keys in Postgres (`DataProtectionKeys`)
   **And** the builder verifies the package (publisher, download count, repository, latest stable version) and pins it exactly (AGENTS.md, Dependency verification)

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- `docker compose -f docker-compose.yml -f docker-compose.aws.yml config` fails without the variables and validates with them.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes (Data Protection key ring in AWS).
- Lane: B.
- Touches: `docker-compose.aws.yml`; `infra/otel-collector/config.yaml`; infra/nginx/templates/ (Mailpit basic auth, origin header check); backend/src/DisputeDesk.Api/Program.cs (Data Protection key storage by configuration); `backend/Directory.Packages.props`.

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md

## Notes

- Gold-plating watch: no extra services beyond the collector.
- Decision: 2026-10-27 — Data Protection keys are encrypted at rest in AWS through `Amazon.AspNetCore.DataProtection.SSM` (AD-7); this item moved here from 7.8a and leaves the architecture Deferred table.
- The instance role's SSM and KMS permissions for the key-ring parameter path belong to the Terraform compute module (7.5).
