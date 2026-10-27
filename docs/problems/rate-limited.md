# Too many requests

- Type: `https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/rate-limited.md`
- Status: 429

## When it happens

A rate limit was reached (RFC 6585 §4). The Api's limits send `Retry-After`; the gateway's coarse per-IP limit carries no `traceId`, because the gateway has no trace context.

## What a client should do

Wait for the `Retry-After` interval, then retry.
