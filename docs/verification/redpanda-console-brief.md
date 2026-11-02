# Redpanda Console profile and Redpanda pin bump (Small tier)

Human decisions of 2026-11-02, from the Redpanda vs Kafka research: add Redpanda Console as an optional, local-only Compose profile, off by default and never in AWS; bump the Redpanda pin from v26.2.2 to v26.2.6. AD-27 is amended to allow this one profile-gated container (architecture.md). Decision record: `docs/decisions/ad-010-redpanda-kafka-api-broker.md`.

## Criteria

1. **Pin bump.** `docker-compose.yml` (`redpanda`, `redpanda-topics`) and `backend/tests/DisputeDesk.Worker.IntegrationTests/Outbox/OutboxFixture.cs` use `docker.redpanda.com/redpandadata/redpanda:v26.2.6` pinned by its multi-arch index digest, read from the registry (`docker buildx imagetools inspect`), never guessed. The Worker integration tests (outbox relay, broker outage) pass on the new image.
2. **Console service.** `redpanda-console` in `docker-compose.yml` with `profiles: [console]`: the latest stable `docker.redpanda.com/redpandadata/console` (the research found v3.13.1, 2026-10-22; confirm on the registry), pinned by tag and multi-arch index digest; `KAFKA_BROKERS=redpanda:9092`; published only on `127.0.0.1`, host port overridable like the others (for example `DISPUTEDESK_CONSOLE_PORT`, default 8081, which nothing else uses); a `mem_limit` sized from a measured idle figure; `depends_on: redpanda (healthy)`; runs as the image's non-root user. No authentication, licence or other enterprise setting (Console needs an Enterprise licence only for SSO, RBAC, debug bundles and partition reassignment; Redpanda docs, *Console enterprise licence*).
3. **Off by default.** `docker compose up --wait` without a profile does not start the console. A check fails if it ever does (extend an existing check rather than add a new script).
4. **Never in AWS.** `infra/aws/check-compose.sh` fails if the AWS render starts the console without a profile or under the `local` profile, and `infra/aws/deploy.sh` never enables the `console` profile.
5. **Works.** With the stack up, `docker compose --profile console up -d --wait redpanda-console` serves the UI on `127.0.0.1:<port>`, and its API lists the `disputedesk.disputes` topic. The report records the command and the result.
6. **Docs.** The `docker-compose.yml` header lists the console URL and its port variable; `infra/README.md` says how to start and stop it, that it is local only, and that it adds a container only when asked.

## Out of scope

Prometheus or Grafana, broker metrics into Aspire, any change to Redpanda's start flags (the production-mode durability note is P75 in `docs/production-path.md`), Kafka.
