---
title: 'technical research: event-versioning'
type: 'technical'
topic: 'event-versioning'
decision: 'Simplest defensible event envelope, schema and versioning approach for DisputeDesk outbox-to-Redpanda events (DisputeRaised, DisputeStatusChanged)'
source: 'native run (headless, single researcher)'
status: complete
preset: 'quick'
validation: 'normal'
created: '2026-10-25'
updated: '2026-10-25'
claims_verified: 3
claims_unverified: 10
---

# Technical research: event versioning

**Decision this research serves:** what is the simplest event envelope, schema and versioning approach we can defend for DisputeDesk's two domain events? They go through a transactional outbox to Redpanda, one Worker relays and consumes them, and the partition key is the dispute ID.

## Executive summary

**Recommendation:**

- **Envelope.** Use CloudEvents 1.0 attribute names (`id`, `source`, `type`, `specversion`, `subject`, `time`, `datacontenttype`, `dataschema`). Send them as one JSON object in the Kafka message value: this is CloudEvents *structured mode*. We take only the names. We add no SDK and no new package.
- **Versioning.** Put the major version in `type`, for example `disputedesk.dispute.status-changed.v1`. Changes within a version are additive only. A breaking change gets a new `.v2` type, and we publish v1 and v2 side by side for a while.
- **Schemas.** Commit the C# records in DisputeDesk.Contracts, plus one JSON Schema file per type. These are the contract. We use no schema registry.
- **Tracing.** Store `traceparent` in the outbox row when the row is written. The relay copies it into a plain Kafka `traceparent` header.
- **Consumers.** Each consumer deduplicates on the event `id` through an inbox table, inside the same transaction as its own changes.

Three findings drive this:

1. **CloudEvents already defines the dedupe key we need.** `source` + `id` must be unique for each distinct event. A re-sent event "MAY have the same `id`", and consumers "MAY assume" that a repeated pair is a duplicate [1]. Microsoft's Idempotent Consumer guidance names that pair as the stable deduplication key [15]. So the CloudEvents names cost nothing and give us a standard to point to.
2. **Versioning in the type name is what the spec itself suggests.** For a compatible change, the type "should generally stay the same". For an incompatible one, the producer changes the type and is "encouraged to produce both the old event and the new event for some time" [4]. A registry enforces compatibility modes (BACKWARD by default) across many independent teams [5][6]. We have one producer and one consumer in one repo, so a contract test does the same job.
3. **The outbox delivers at least once, by design.** The relay "might publish a message more than once" [13]. Kafka's idempotent producer only covers the client's internal retries, and librdkafka (the library under the .NET client) has it off by default [16]. Consumer dedupe on `id` is therefore required, not optional [15].

**Biggest caveat:** OpenTelemetry's messaging conventions are still *Development*, not Stable [7]. The .NET Kafka instrumentation is a prerelease package, and its documentation does not say whether it writes trace headers [12]. Inject `traceparent` by hand (about ten lines) rather than depend on that package.

## 1. Envelope and CloudEvents

CloudEvents 1.0 requires `id`, `source`, `specversion` (value `1.0`) and `type`. It defines `datacontenttype`, `dataschema`, `subject` and `time` as optional [1]. The semantics that matter here:

- `subject` names the thing the event is about, "in the context of the event producer". Middleware can filter on it without reading `data` [1]. The dispute ID fits here (our inference).
- `time` is RFC 3339 [1].
- `type` "might include information such as the version". The spec's own example is `com.example.object.deleted.v2` [1].
- `dataschema` should change URI when the schema changes incompatibly [1].

The Kafka protocol binding defines two modes [2]:

- **Structured mode:** attributes and data together in the value, with `content-type: application/cloudevents+json`.
- **Binary mode:** `data` goes in the value "as-is", and each attribute goes in its own header with a `ce_` prefix, such as `ce_id` or `ce_type`.

Either way, the binding maps the producer's record key to the Kafka key by default [2], so the dispute-ID key needs no extension. (The spec pages are marked `1.0.3-wip` on the main branch; the wire `specversion` is still `1.0` [1][2].)

**Is it worth adopting the names?** Yes, as names. Our own envelope would need the same fields anyway: eventId, eventType, occurredAt, aggregate ID. CloudEvents gives each one a defined meaning, including the duplicate rule. **Structured mode** is the simpler choice. It keeps the whole event in one JSON document, which is what the outbox row already stores, what Redpanda Console shows, and what a JSON Schema validates. Binary mode spreads the envelope across headers, so the outbox would need a header map and the consumer would rebuild the event. Its advantage, routing without parsing the value, does not matter with a single consumer. The CloudEvents .NET SDK is not needed to produce this JSON. Leave it out.

**Field mapping**, from the names you asked about to CloudEvents:

| Asked for | CloudEvents | Value |
|---|---|---|
| eventId | `id` | UUID set when the outbox row is written, so retries keep it |
| eventType + schemaVersion | `type` | `disputedesk.dispute.raised.v1`. Do not add a separate schemaVersion field: the major version lives in the type and minor changes are additive |
| occurredAt | `time` | From `TimeProvider`, at the moment the change commits |
| aggregate / dispute ID | `subject` (also `data.disputeId`, and the Kafka key) | Dispute ID |
| producer | `source` | A fixed URI reference such as `/disputedesk/api` |
| correlation / trace | Kafka header `traceparent` | See section 3. Do not put it in the envelope |

`type` should use a reverse-DNS prefix (SHOULD, not MUST) [1]. Use one if the project owns a domain name. Otherwise use a plain `disputedesk.` prefix and record the deviation.

## 2. Versioning and schema registry

The CloudEvents primer leaves the versioning scheme to the producer. It also says that compatible changes keep the type, that incompatible changes change it, and that both versions should be published during a transition. It treats `dataschema` as informational [4]. That maps directly to the rules here:

- **Within a version:** add optional fields only. Never rename, remove, or change the type or meaning of a field. Consumers ignore fields they do not know. (That last rule is a reader policy we choose. We found no authoritative rule for additive change to plain JSON. Confluent covers it only through JSON Schema open and closed content models [5].)
- **Breaking change:** create a new `.v2` type with its own C# record and schema file. The producer writes both types for a period, consumers move to v2, then v1 stops.

**Registries:**

- **Confluent Schema Registry** checks each new schema against earlier versions. Its modes are BACKWARD (the default), FORWARD, FULL, the `_TRANSITIVE` variant of each, and NONE. It supports Avro, Protobuf and JSON Schema [5].
- **Redpanda** builds a registry into the broker binary. It supports the same three formats and the same modes, with BACKWARD as the default [6].

Microsoft's choreography guidance recommends a registry when "multiple independent services consume the same events" [20]. That is not our case. **Recommendation: no registry.** The contract is the C# record in DisputeDesk.Contracts plus a committed JSON Schema (`contracts/events/<type>.schema.json`). A unit test checks that serialized samples validate against the schema and that v1 sample JSON still deserializes. Redpanda's registry needs no extra container, so moving to it later is cheap. It goes in the production path.

## 3. Trace context through the outbox

- **Header format.** W3C Trace Context Level 1 has been a Recommendation since November 2021. Its `traceparent` header is `version-traceid-parentid-flags`, for example `00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01` [9].
- **.NET.** `Activity` uses W3C IDs by default. ASP.NET Core and HttpClient propagate them automatically, but other protocols are left to libraries [10]. `DistributedContextPropagator.Inject(activity, carrier, setter)` writes the context into any carrier [11].
- **Kafka client.** Confluent.Kafka has no built-in tracing. The OpenTelemetry contrib instrumentation is *Development* and prerelease, and its README never mentions headers [12].
- **OpenTelemetry guidance.** "A producer SHOULD attach a message creation context to each message". Consumer spans should *link* to that context by default. Making it the parent is an opt-in, and only for single-message processing [7]. The conventions are still Development [7]. Use `messaging.system=kafka`, `messaging.destination.name`, `messaging.operation.type`, `messaging.message.id` and `messaging.kafka.message.key`, and expect the names to change [7][8].

**Mechanism:**

1. When the API writes the outbox row, in the same transaction as the dispute change, store `Activity.Current`'s context in a `traceparent` column (plus a nullable `tracestate`). This is the creation context: the request that caused the event.
2. The relay starts a PRODUCER `send` span with that stored context as parent. It injects the span's context into the Kafka headers as `traceparent`, using the propagator.
3. The consumer extracts the header and starts a CONSUMER `process` span that links to the extracted context. Setting it as parent is also allowed, because the consumer handles one message at a time.

The CloudEvents distributed-tracing extension "is not intended to replace the protocol specific headers for tracing" and has no stated maturity [3]. Skip it. The plain `traceparent` header is what OpenTelemetry tooling reads.

## 4. Consumer idempotency

- **Duplicates are expected.** The outbox guarantees publication only if the transaction commits, and the relay may publish twice [13]. Microsoft lists Kafka among at-least-once brokers. Duplicates come from producer retries, missed acknowledgements and consumer crashes [15].
- **Producer setting.** Set `EnableIdempotence = true` on the producer. The librdkafka default is `false` [16], while the Java client defaults to `true` [17], so do not assume. It stops broker-retry duplicates. It does not stop the relay re-sending a row after a crash between producing and marking the row published (our inference from the scope of the setting [16][17]).
- **Inbox table.** The consumer inserts `(consumer_name, event_id)` into an `inbox` table with a unique constraint, in the same transaction as its side effects. A conflict means the event is a duplicate: acknowledge it and skip [14][15]. Key on the consumer name as well, so a second consumer later is not blocked by the first [15].
- **Retention.** Keep inbox rows at least as long as redelivery is possible [15]. With Kafka that means the topic's retention period.
- **Natural idempotency.** Carry the resulting state (`toStatus`), not a delta, so that applying an event twice gives the same result [15]. Optionally add the dispute's version number so that stale or out-of-order events can be rejected [15]. Ordering already holds per dispute because the key is the dispute ID. Keep **both event types on one topic**, or that ordering is lost between them.

## 5. DisputeStatusChanged payload and POPIA

POPIA section 10 lets personal information be processed only if it is "adequate, relevant and not excessive" for the purpose [18]. (The source is an unofficial reproduction of the Act; confirm against the gazetted text.) Fowler separates two styles [19]:

- **Event Notification:** a thin payload with IDs, so the receiver calls back for details.
- **Event-Carried State Transfer:** the changed data travels in the event, at the cost of "lots of copies".

Every copy in a Kafka log is another place personal information sits until retention expires. Our inference: carry state about the dispute, never about the person.

**Fields:**

- **Include:** `disputeId`, `fromStatus`, `toStatus`, `action` (the domain verb that caused the change), `actorType` (`Customer`, `Agent` or `System`), `occurredAt`, and optionally `disputeVersion`.
- **Exclude:** names, ID numbers, phone numbers, email addresses, account numbers, amounts linked to a person, actor user IDs, and **the visible note**. The note is free text and can contain anything. A consumer that needs it reads it through the API, which enforces access control.
- **`occurredAt`:** keep it in `data` even though `time` repeats it, so the payload stands on its own when the C# record is used without the envelope. This is a judgement call; dropping it is also defensible.

## Cross-dimension insights

- **One UUID does three jobs.** The outbox row's `id` is the CloudEvents `id`, the OpenTelemetry `messaging.message.id`, and the inbox dedupe key [1][7][15]. Generating it when the row is written, not when the relay sends, is what makes dedupe work.
- **Absolute state serves two goals.** Carrying `toStatus` instead of a delta makes consumers naturally idempotent [15]. Keeping the payload to dispute state supports POPIA minimality [18][19].
- **The topic layout follows from the key.** The dispute-ID partition key guarantees order only within one topic, so both event types belong on one topic.

## Recommendations

| # | Recommendation | Feeds | Confidence basis |
|---|---|---|---|
| R1 | CloudEvents 1.0 names, structured-mode JSON value, no SDK | Architecture: event contract | High [1][2], verified dedupe semantics [15] |
| R2 | Major version in `type`. Additive-only within a version. A breaking change means a new type, published side by side | Architecture, Contracts project | Medium: primer is a single source [4] |
| R3 | No schema registry. C# records plus committed JSON Schema plus a contract test. Registry goes in the production path | Architecture, `docs/production-path.md` | Medium [5][6][20] |
| R4 | `traceparent` column in the outbox. Relay injects a Kafka header. Consumer span links to it. Manual propagator, no prerelease instrumentation package | Observability story | Medium: OpenTelemetry conventions are Development [7], instrumentation behaviour unknown [12] |
| R5 | Inbox table keyed `(consumer_name, event_id)` in the consumer's own transaction, plus `EnableIdempotence = true` | Worker story | High: verified [13][14][15] |
| R6 | Thin DisputeStatusChanged as in the JSON example. No note, no personal data | Contracts, security review | Medium: POPIA source is an unofficial reproduction [18] |

**Recommended envelope and payload:**

```json
{
  "specversion": "1.0",
  "id": "0b9f6f0e-5c1e-4f5e-9a51-3a2d6c1b7e42",
  "source": "/disputedesk/api",
  "type": "disputedesk.dispute.status-changed.v1",
  "subject": "dsp_01JB7Q8M2X",
  "time": "2026-10-25T09:14:03.512Z",
  "datacontenttype": "application/json",
  "dataschema": "contracts/events/disputedesk.dispute.status-changed.v1.schema.json",
  "data": {
    "disputeId": "dsp_01JB7Q8M2X",
    "fromStatus": "Submitted",
    "toStatus": "UnderReview",
    "action": "StartReview",
    "actorType": "Agent",
    "occurredAt": "2026-10-25T09:14:03.512Z",
    "disputeVersion": 3
  }
}
```

The Kafka record has key `dsp_01JB7Q8M2X` and headers `content-type: application/cloudevents+json` and `traceparent: 00-<trace-id>-<span-id>-01`. DisputeRaised uses the same envelope with type `disputedesk.dispute.raised.v1`. Its data is limited to dispute facts such as `disputeId`, a category or reason code, `channel` and `occurredAt`, with no customer identifiers.

**Production path:**

- Redpanda or Confluent Schema Registry, using JSON Schema or Protobuf with BACKWARD_TRANSITIVE checks in CI.
- Binary content mode, if routing on headers becomes useful.
- The CloudEvents .NET SDK.
- The OpenTelemetry Kafka instrumentation once it is stable.
- A cleanup job for inbox and outbox rows tied to topic retention.
- A dead-letter topic.
- Kafka transactions or exactly-once semantics, for Kafka-to-Kafka flows only.
- Formal deprecation windows for v1 and v2 running side by side.
- Encrypting fields or tokenising personal data, if a future event must carry it.

## Open questions

- Does `OpenTelemetry.Instrumentation.ConfluentKafka` inject or extract `traceparent` automatically? Its README says nothing [12]. Answer by reading the package source or doing a quick spike. It does not change the recommendation.
- Does the gazetted text of POPIA section 10 match the reproduction used here, and do sections 14 (retention) and 19 (security safeguards) set limits on Kafka log retention? Answer from the Information Regulator's text.
- Is W3C Trace Context Level 2 now a Recommendation? Only Level 1 was checked. This does not matter for `traceparent` version `00`.
- We found no authoritative, format-neutral rule for additive change to plain JSON. The "ignore unknown fields" reader rule is our own policy.

## Source appendix

| # | Supports | Publisher | Pub date | Accessed | Confidence |
|---|---|---|---|---|---|
| [1] | CloudEvents attributes, the source+id duplicate rule, version in type, dataschema | [CNCF CloudEvents spec](https://github.com/cloudevents/spec/blob/main/cloudevents/spec.md) | undated (main) | 2026-10-25 | high |
| [2] | Kafka binding: structured and binary modes, `ce_` prefix, key mapping | [CNCF CloudEvents Kafka binding](https://github.com/cloudevents/spec/blob/main/cloudevents/bindings/kafka-protocol-binding.md) | undated | 2026-10-25 | high |
| [3] | Distributed-tracing extension does not replace protocol headers | [CNCF CloudEvents extension](https://github.com/cloudevents/spec/blob/main/cloudevents/extensions/distributed-tracing.md) | undated | 2026-10-25 | medium |
| [4] | Primer on versioning and side-by-side publishing | [CNCF CloudEvents primer](https://github.com/cloudevents/spec/blob/main/cloudevents/primer.md) | undated | 2026-10-25 | high |
| [5] | Compatibility modes and formats; JSON Schema content models | [Confluent docs](https://docs.confluent.io/platform/current/schema-registry/fundamentals/schema-evolution.html) | undated (current) | 2026-10-25 | high |
| [6] | Redpanda's built-in schema registry | [Redpanda docs](https://docs.redpanda.com/current/manage/schema-reg/schema-reg-overview/) | undated (current) | 2026-10-25 | high |
| [7] | Messaging conventions status (Development), creation context, links, attributes | [OpenTelemetry semconv](https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/) | undated (current) | 2026-10-25 | high |
| [8] | Kafka attributes; keys are not unique | [OpenTelemetry semconv Kafka](https://opentelemetry.io/docs/specs/semconv/messaging/kafka/) | undated | 2026-10-25 | high |
| [9] | `traceparent` format; Level 1 Recommendation | [W3C Trace Context](https://www.w3.org/TR/trace-context/) | 2021-11-23 | 2026-10-25 | high |
| [10] | .NET Activity W3C IDs; non-HTTP propagation left to libraries | [Microsoft Learn](https://learn.microsoft.com/dotnet/core/diagnostics/distributed-tracing-concepts) | undated | 2026-10-25 | high |
| [11] | `DistributedContextPropagator.Inject` | [Microsoft Learn API](https://learn.microsoft.com/dotnet/api/system.diagnostics.distributedcontextpropagator.inject) | undated | 2026-10-25 | high |
| [12] | Kafka instrumentation is Development and prerelease; README silent on headers | [OpenTelemetry .NET contrib](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/tree/main/src/OpenTelemetry.Instrumentation.ConfluentKafka) | undated | 2026-10-25 | medium |
| [13] | Outbox relay may publish more than once | [microservices.io](https://microservices.io/patterns/data/transactional-outbox.html) | undated | 2026-10-25 | high |
| [14] | Processed-messages table in the same transaction | [microservices.io](https://microservices.io/patterns/communication-style/idempotent-consumer.html) | undated | 2026-10-25 | high |
| [15] | Idempotent consumer: source+id key, inbox, unique constraint, composite key, retention, absolute state, version for staleness | [Microsoft Learn](https://learn.microsoft.com/azure/architecture/patterns/idempotent-consumer) | undated (current) | 2026-10-25 | high |
| [16] | librdkafka `enable.idempotence` defaults to false | [librdkafka CONFIGURATION.md](https://github.com/confluentinc/librdkafka/blob/master/CONFIGURATION.md) | undated | 2026-10-25 | high |
| [17] | Java producer idempotence default and scope | [Confluent producer configs](https://docs.confluent.io/platform/current/installation/configuration/producer-configs.html) | undated | 2026-10-25 | high |
| [18] | POPIA section 10 minimality | [popia.co.za (unofficial reproduction)](https://popia.co.za/section-10-minimality/) | undated | 2026-10-25 | medium |
| [19] | Event Notification vs Event-Carried State Transfer | [martinfowler.com](https://martinfowler.com/articles/201701-event-driven.html) | 2017-02-07 | 2026-10-25 | high |
| [20] | Registry advised when many independent consumers share events | [Microsoft Learn](https://learn.microsoft.com/azure/architecture/patterns/choreography) | undated | 2026-10-25 | high |

## Staleness map

Windows (months): version-status 1, tooling 6, regulatory 12, spec-semantics 24, pattern 24. Undated current docs are dated by access date (2026-10-25).

| Claim | Class | Re-check |
|---|---|---|
| [7] OpenTelemetry messaging conventions status (Development) | version-status | 2026-11-25 |
| [12] OpenTelemetry Kafka instrumentation Development/prerelease, header behaviour | version-status | 2026-11-25 |
| [3] CloudEvents tracing extension status | version-status | 2026-11-25 |
| [16] librdkafka `enable.idempotence` default | tooling | 2027-04-25 |
| [5][6] Registry modes and formats; Redpanda built-in registry | tooling | 2027-04-25 |
| [18] POPIA s10 minimality | regulatory | 2027-10-25 |
| [1][2][4] CloudEvents 1.0 semantics and Kafka binding | spec-semantics | 2028-10-25 |
| [13][14][15] Outbox at-least-once; idempotent consumer | pattern | 2028-10-25 |

Earliest re-check: **2026-11-25** (OpenTelemetry messaging and instrumentation status). Refresh handles it.
