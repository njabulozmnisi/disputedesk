# Internal error

- Type: `https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/internal.md`
- Status: 500, or another 5xx that has no slug of its own

## When it happens

An unexpected failure. The body carries only the `traceId`, never exception details.

## What a client should do

Retry once later; if it persists, report the `traceId`.
