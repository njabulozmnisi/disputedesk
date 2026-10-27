# Validation failed

- Type: `https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/validation.md`
- Status: 400, or another 4xx the request itself causes (for example 405 or 415), which keeps its own status

## When it happens

The request is malformed or a field breaks a rule: a missing or unparsable parameter, a value out of range, a note over its length, a missing antiforgery token, or a malformed `Idempotency-Key`. `errors` names each field when one is known.

## What a client should do

Fix the named fields and send the request again. Do not retry unchanged.
