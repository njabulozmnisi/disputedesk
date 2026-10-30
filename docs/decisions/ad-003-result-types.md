# ad-003: Result types for expected failures

**Status:** accepted (records AD-3, extended by stories 2.7, 2.4 and 6.5)

**Context.** Many endpoints refuse requests for expected reasons: not found, not allowed in this status, a business rule, a dependency down. Without one rule, stories invent their own status codes and problem types, framework responses go out without a body, and the SPA ends up routing on message text (AD-3, Prevents).

**Decision.** Expected failures are values, mapped to HTTP in one place (AD-3).

- Domain and Application return `Result` with an `Error` for expected failures and throw only for the unexpected.
- `ErrorKind` is a closed set, each with one status: `NotFound` 404, `Validation` 400 (with field errors), `Forbidden` 403, `ActionNotAllowed` 409 (adds the caller's view of the current status), `Conflict` 409, `BusinessRule` 422, `Unavailable` 503 and `RateLimited` 429 (decided 2026-10-30, story 6.5).
- `ErrorResults.ToProblem` is the only place an `Error` becomes HTTP: an RFC 9457 problem body with `type`, `traceId` and camelCase `errors` keys, set in `CustomizeProblemDetails`. Errors never echo a value.
- `type` is a resolvable URL, `https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/<slug>.md`, whose page says when the problem happens and what the client should do (RFC 9457 §3.1.1; decided 2026-10-27, story 2.7).
- Slugs are constants in Contracts, one class per feature; a test asserts every Domain and Application error code is in the catalogue.
- Framework responses get the same bodies: the cookie and JwtBearer challenge and forbid events, the rate limiter's `OnRejected`, antiforgery failures and `UseStatusCodePages` for every other bodiless error.
- The SPA identifies a problem by its full `type` URI, compared with `===` (decided 2026-10-28, story 2.4).

**Options rejected** (AD-3).

- *Exceptions for control flow:* exceptions are kept for the unexpected.
- *FluentResults or ErrorOr:* rejected in AD-3; `Result` and `Error` are the project's own small types, with no package.
- *Tag URIs* (`tag:disputedesk,2026:problem:<slug>`, used until 2026-10-27): nothing resolves them, and `disputedesk` is not the fully qualified authority RFC 4151 requires.
- *`invalid-transition`:* the glossary rules out "transition"; the kind is `ActionNotAllowed`.

**Consequences.**

- A new expected failure is one `Error` with an existing kind and one slug constant; the catalogue test fails if the slug is missing.
- Every refusal, from a handler or from the framework, reaches the client in one body shape, so the SPA and operations tools read the same fields everywhere (AD-31).
- One exception to the body shape: the gateway's own 429 carries no `traceId`, because nginx has no trace context (AD-3, AD-23; decided 2026-10-27).
- The `type` URLs live under the GitHub namespace, an accepted risk: a rename, deletion or repo-jacking would break or hijack them. Moving them to an owned domain is in `docs/production-path.md` (2.7 security review, 2026-10-27).
