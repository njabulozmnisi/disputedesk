# Runbook

These procedures are for the local Compose stack only. An AWS deployment's database is private and is not reached
this way ([infra/terraform/README.md](../terraform/README.md) covers the AWS environment). Run every command
from the repo root, with the stack up (`docker compose up --wait`).

## 1. Release a `Failed` outbox row

The outbox relay sets a row `Failed` after 5 permanent refusals by Kafka (AD-17). Locally the relay runs inside the
Api, which logs
`Outbox event <event ID> of dispute <dispute ID> is Failed after 5 permanent refusals`, the
`disputedesk.outbox.failed` metric counts it (Aspire Dashboard, Metrics), and the dispute's later events wait.

This procedure is AD-22's one operator exception to "`OutboxMessages` is updated only by the relay". It never sets a
row to `Sent` and never deletes a row. The Api needs no restart: the relay picks the row up on its next loop. It runs
as the owner role `disputedesk` and writes no audit record, which is accepted for the local stack; a dedicated operator
role and an audited release are in [the production path](../../docs/production-path.md) (P51).

1. **Find the row and its blocked successors**, with the read-only role:

   ```sh
   docker compose exec postgres psql -U qa_readonly -d disputedesk
   ```

   ```sql
   SELECT "Id", "DisputeId", "DisputeVersion", "Attempts", "LastError" FROM "OutboxMessages" WHERE "Status" = 'Failed';
   \set dispute_id '<dispute ID>'
   SELECT "Id", "DisputeVersion", "Status" FROM "OutboxMessages" WHERE "DisputeId" = :'dispute_id'::uuid ORDER BY "DisputeVersion";
   ```

   The rows with a higher `DisputeVersion` than the `Failed` row are its blocked successors.

2. **Fix the cause from `LastError`, outside the row.** `Payload`, `Id` and every other column stay as they are: the
   payload is the dispute event's CloudEvent byte for byte, whose `id` is the stored event ID, and consumers
   deduplicate by that `id` (AD-14, AD-17).

   For `MsgSizeTooLarge`, compare the payload's size with the topic's limit. In the same read-only session:

   ```sql
   \set event_id '<event ID>'
   SELECT octet_length("Payload") FROM "OutboxMessages" WHERE "Id" = :'event_id'::uuid;
   ```

   ```sh
   docker compose exec redpanda rpk topic describe disputedesk.disputes -c
   ```

   - A payload over the topic's current `max.message.bytes` but well under 1,000,000 bytes (the topic limit was
     lowered): raise or restore the topic's limit, for example back to the broker default with
     `docker compose exec redpanda rpk topic alter-config disputedesk.disputes --delete max.message.bytes`.
   - A payload at or near 1,000,000 bytes hits the relay's own client limit (librdkafka's `message.max.bytes`
     default), which no topic change fixes. Do not release it.

   A row whose payload itself is the cause stays `Failed`; it is not released ([production path](../../docs/production-path.md),
   P20).

3. **Release the row**, as the `disputedesk` role, in an interactive session (no password on the command line):

   ```sh
   docker compose exec postgres psql -U disputedesk -d disputedesk
   ```

   ```sql
   \set ON_ERROR_STOP on
   \set event_id '<event ID>'
   BEGIN;
   UPDATE "OutboxMessages" SET "Status" = 'Pending', "Attempts" = 0, "NextAttemptAtUtc" = now() WHERE "Id" = :'event_id'::uuid AND "Status" = 'Failed';
   ```

   If psql printed `UPDATE 1`, type `COMMIT;`. Otherwise type `ROLLBACK;`. psql quotes the variable, and the `uuid`
   cast rejects a malformed ID. In an interactive session `ON_ERROR_STOP` does not end the session; after an error
   inside the transaction, `COMMIT` reports `ROLLBACK`, so type `ROLLBACK;`. `LastError` stays as the record of the
   last refusal; the relay overwrites it if the row is refused again.

4. **Verify.** Within a minute the row is `Sent` (step 1's second query). Then read the topic to its current end:

   ```sh
   docker compose exec redpanda rpk topic consume disputedesk.disputes -o :end -f '%k %o %v\n' | grep '<dispute ID>'
   ```

   Each line is the key (the dispute ID), the offset and the CloudEvent, whose `id` is the event ID. The released
   event's record has a lower offset than the record of the dispute's next version, which proves the relay kept the
   dispute's order (AD-17).

## 2. Core banking unavailable

The core-banking stub stands in for the bank's core banking. Its client retries and then opens a circuit breaker
(AD-21); once the policies are exhausted the Api answers 503.

1. Signed in as a customer at <http://localhost:8080>, open the raise form for a disputable transaction.
2. Stop the stub:

   ```sh
   docker compose stop core-banking
   ```

3. Submit the raise. It answers 503 with problem `type` ending `core-banking-unavailable.md` within about 5 seconds,
   and the SPA shows "We can't check this transaction with the bank right now. Try again." The transactions request
   `GET /api/v1/transactions` answers the same 503.
4. What stays up: `/health/ready` stays healthy, because readiness does not check core banking (AD-25), and the dispute
   history still loads, because it reads only what the raises stored.

   ```sh
   curl -sS -w '\n%{http_code}\n' http://localhost:8080/api/health/ready
   ```

5. Where the operator sees it: the Aspire Dashboard (<http://localhost:18888>) shows the failed spans to `core-banking`
   under the Api's traces, and the Api's logs (`docker compose logs --no-color api`) show the refused calls through
   the resilience handler.
6. Restore it:

   ```sh
   docker compose start core-banking
   ```

   Once the circuit breaker closes (at most 15 seconds) the transactions load again.

## 3. Reset the local stack

> **Destructive, local only.** `docker compose down -v` deletes every volume: Postgres, Redpanda, Zitadel and the
> generated credentials. It is never a troubleshooting step on a shared or deployed environment.

```sh
docker compose down -v
docker compose up --wait
```

The stack comes back with fresh credentials and the seed written again, its dates relative to the new start-up.

## 4. Transient Kafka outage

While Redpanda is down the relay keeps each new event `Pending` and counts no attempt (AD-17). Raising still answers
201, and `/health/ready` stays healthy, because readiness does not check Kafka (AD-25).

1. **Where it shows.** The Api, which runs the relay locally, logs `Outbox relay cannot reach kafka (…)` and `Outbox
   relay could not send an event (…); it stays Pending and is retried` (`docker compose logs --no-color api`); the
   Aspire Dashboard shows failed `send disputedesk.disputes` spans under the Api's traces.
2. **Count the rows not yet `Sent`** in procedure 1's read-only session (`psql -U qa_readonly`). In a drill, take this
   count before stopping Redpanda as the baseline. `"DisputeId"` of the `Pending` rows names the affected disputes.

   ```sql
   SELECT "Status", "Attempts", count(*) FROM "OutboxMessages" WHERE "Status" <> 'Sent' GROUP BY 1, 2 ORDER BY 1, 2;
   ```

   `Pending` rows with 0 attempts are the transient backlog, which the relay sends by itself. `Pending` rows with 1 to 4
   attempts are a permanent refusal in backoff and turn `Failed` at 5. A `Failed` row needs procedure 1; procedure 1 is
   never used for a `Pending` row.
3. **Recover** with `docker compose start redpanda`. No row is updated and the stack is not reset (no `down -v`).
4. **Drain check**, repeated for at most 5 minutes: step 2's count is back to the baseline, and procedure 1's step 4
   `rpk topic consume` shows each affected dispute's events in the order they were written (offsets rise with
   `disputeVersion`). This holds for one relay's recovery; delivery stays at least once (AD-17).
