# ad-010: Redpanda as the Kafka-API broker

**Status:** accepted (2026-11-02)

**Context.** Every dispute change reaches the rest of the bank as an event (PRD §4.9, FR-27, FR-28). The transactional outbox commits the stored event, the `Disputes` read model and the outbox row together, and one relay publishes them at least once, keyed by dispute ID, with `traceparent` in the Kafka headers (AD-17, AD-19, AD-20; [Transactional outbox](https://microservices.io/patterns/data/transactional-outbox.html)). No consumer lives in this repo (P16), so in v1 the broker proves the integration contract against a real Kafka-API broker: CloudEvents over the Kafka binding, per-dispute order, at-least-once delivery with a stable `id`, outage behaviour, and one trace across the async boundary. The broker was named in the kickoff specification; nothing recorded why until now.

**Decision.** Keep Redpanda as the local and AWS dev broker, behind the Kafka protocol only. The relay uses `Confluent.Kafka` with no Redpanda-specific API, so the broker stays replaceable by Apache Kafka or a managed Kafka service without a production code change ([Redpanda, Kafka compatibility](https://docs.redpanda.com/current/develop/kafka-clients/)).

| Area | Redpanda (v26.2.x) | Apache Kafka 4.x (KRaft) |
| --- | --- | --- |
| Client compatibility | Kafka protocol; librdkafka validated; idempotent producer and `Acks.All` proven by this repo's relay tests | Reference implementation |
| Footprint | One C++ binary, no JVM; 276 MiB idle measured locally (P0-2 smoke); 512 MB in the AWS override | JVM broker; 1 GiB default heap ([Kafka 4.4 source](https://github.com/apache/kafka/tree/4.4.0)) |
| Local setup | `--mode=dev-container --smp=1`; `Testcontainers.Redpanda` | KRaft combined mode, about ten settings; `Testcontainers.Kafka` |
| Tooling in the image | `rpk` (health check, topic creation, the e2e helper), Admin API, Prometheus metrics on 9644, built-in schema registry ([Monitor Redpanda](https://docs.redpanda.com/current/manage/monitoring/)) | `kafka-*.sh`, JMX; registry and UI are separate products |
| Web UI | Redpanda Console, free for community features ([Console enterprise licence](https://docs.redpanda.com/streaming/current/console/config/enterprise-license/)) | kafbat/kafka-ui, AKHQ (Apache 2.0) |
| Licence | Core under BSL 1.1: free to use, not to resell as a streaming service; enterprise features under the RCL ([Licensing overview](https://docs.redpanda.com/current/get-started/licensing/overview/)) | Apache 2.0 |
| Maturity | Younger, one vendor; Jepsen 2022 findings were fixed ([Jepsen](https://jepsen.io/analyses/redpanda-21.10.1)) | Long-lived Apache project, widest operator knowledge |

Memory is the binding constraint: the AWS dev instance has 4 GiB and the AWS Compose limits leave 26 MiB of headroom (`infra/deploy/aws/check-compose.sh`), so a JVM broker at its defaults does not fit without cuts elsewhere.

**Options rejected.**

- *Apache Kafka 4.x:* larger at its defaults on a memory-bound instance, and the project uses nothing Kafka adds. Switching costs configuration and tests only (Compose, the AWS override, `OutboxFixture.cs`, the e2e helper that runs `rpk`, `check-compose.sh`, docs).
- *Amazon MSK:* excluded by the kickoff specification, and it needs Terraform, VPC subnets in two or three zones, and a cost beyond the free plan.
- *No broker (consumers poll the outbox or an events feed), SQS or SNS FIFO, EventBridge:* each changes the PRD, which names Kafka headers (FR-27, FR-28, NFR-OBS-1); polling couples consumers to this database and has no global position (P17); SQS cannot replay a deleted message for a new consumer.
- *Prometheus, Grafana or a second OpenTelemetry Collector for broker metrics:* more containers for one broker with one producer; Aspire already shows the producer spans and `disputedesk.outbox.failed`.

**Consequences.**

- Redpanda Console is available as an opt-in local Compose profile, `console`, off by default and never in AWS (AD-27, amended 2026-11-02). It reads `disputedesk.disputes` through the Kafka protocol with no authentication or enterprise feature enabled.
- `--mode=dev-container` bypasses fsync and enables write caching and topic auto-creation; with one replica an acknowledged event can be lost on a broker crash after the relay marked its row `Sent`. Accepted for synthetic data; P75 in `docs/production-path.md` records the production settings.
- The pin follows the latest patch release (v26.2.6 from 2026-11-02) by tag and digest, in Compose and in the Worker integration tests' fixture together.
- Revisit if the bank's integration standard is Apache Kafka or a managed Kafka service, or a reviewer requires an Apache 2.0 broker: the producer code does not change.

Research basis: Redpanda vs Kafka comparison of 2026-11-02 (50 sources), held outside the repo.
