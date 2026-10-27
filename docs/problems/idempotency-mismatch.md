# Idempotency key reused

- Type: `https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/idempotency-mismatch.md`
- Status: 422

## When it happens

The `Idempotency-Key` was already used with a different request body (draft-ietf-httpapi-idempotency-key-header).

## What a client should do

Do not retry with this key. Start a new draft, which gets a new key.
