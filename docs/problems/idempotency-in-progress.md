# Request in progress

- Type: `https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/idempotency-in-progress.md`
- Status: 409

## When it happens

A request with the same `Idempotency-Key` is still being processed (draft-ietf-httpapi-idempotency-key-header).

## What a client should do

Wait about a second and resend the same request with the same key.
