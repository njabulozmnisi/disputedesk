# Problem types

Every DisputeDesk error is an RFC 9457 problem details response whose `type` is the URL of one of these pages (AD-3). Clients route on the slug, the page name without `.md`.

| Slug | Status | Title |
|---|---|---|
| [`validation`](validation.md) | 400 | Validation failed |
| [`not-found`](not-found.md) | 404 | Not found |
| [`action-not-allowed`](action-not-allowed.md) | 409 | Action not allowed |
| [`unauthorized`](unauthorized.md) | 401 | Not signed in |
| [`forbidden`](forbidden.md) | 403 | Forbidden |
| [`rate-limited`](rate-limited.md) | 429 | Too many requests |
| [`internal`](internal.md) | 500 | Internal error |
| [`already-disputed`](already-disputed.md) | 409 | Already disputed |
| [`not-disputable`](not-disputable.md) | 422 | Not disputable |
| [`idempotency-in-progress`](idempotency-in-progress.md) | 409 | Request in progress |
| [`idempotency-mismatch`](idempotency-mismatch.md) | 422 | Idempotency key reused |
| [`linking-required`](linking-required.md) | 403 | Linking required |
| [`already-linked`](already-linked.md) | 403 | Already linked |
| [`otp-incorrect`](otp-incorrect.md) | 422 | Code incorrect |
| [`linking-locked`](linking-locked.md) | 409 | Linking locked |
| [`core-banking-unavailable`](core-banking-unavailable.md) | 503 | Core banking unavailable |
