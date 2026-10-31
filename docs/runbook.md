# Runbook

These procedures are for the local Compose stack only. An AWS deployment's database is private and is not reached
this way. Run every command from the repo root, with the stack up (`docker compose up --wait`).

## 1. Release a `Failed` outbox row

The outbox relay sets a row `Failed` after 5 permanent refusals by Kafka (AD-17). The Worker logs
`Outbox event <event ID> of dispute <dispute ID> is Failed after 5 permanent refusals`, the
`disputedesk.outbox.failed` metric counts it (Aspire Dashboard, Metrics), and the dispute's later events wait.

This procedure is AD-22's one operator exception to "`OutboxMessages` is updated only by the relay". It never sets a
row to `Sent` and never deletes a row. The Worker needs no restart: the relay picks the row up on its next loop. It runs
as the owner role `disputedesk` and writes no audit record, which is accepted for the local stack; a dedicated operator
role and an audited release are in [the production path](production-path.md) (P51).

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

   A row whose payload itself is the cause stays `Failed`; it is not released ([production path](production-path.md),
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
