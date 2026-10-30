---
id: 2
type: story
title: "OTP verification links the login, with lockout and race safety"
parent: epic-6-linking
covers: [R5, R7, R10, R31, R36]
after: [1]
refined: false
hitl: false
risk: high
---

# OTP verification links the login, with lockout and race safety

## Description

Adds `POST /api/v1/linking/verify`: one atomic conditional attempt update on the login's current challenge, a constant-time HMAC comparison on every attempt, then, for a correct code, the challenge consumed and the link inserted in one EF Core transaction; the session keeps its key, and the next request sees the linked customer (AD-12, AD-7; human decision, 2026-10-30). A correct code on the third attempt links; the challenge locks after three wrong codes (human decision, 2026-10-30). Every kind of challenge (claimed, blank, released, expired) answers and counts like a wrong code; a login with no challenge row gets the wrong-code answer without counting.

## Requirements

Verify: Integration tests cover a correct code with the link, the session kept unchanged, the attempt sequence and lockout, every kind of challenge and a login with none, the input bounds, parallel verifications, two logins racing, and the audit entries, metrics and privacy; `openapi.json` changes only by the verify operation and its request schema.

1. **A correct code links in one transaction** (R7; FR-5, FR-6, NFR-DATA-5)
   **Given** customer D's login with a current claimed challenge, verified before its `ExpiresAtUtc` (`TimeProvider`)
   **When** it posts the correct OTP
   **Then** it gets 204 with an empty body, and in one explicit EF Core transaction (Microsoft Learn, EF Core *Transactions*) a conditional update sets `ConsumedAtUtc` only where the row is this challenge and `ConsumedAtUtc` and `SupersededAtUtc` are null (exactly 1 row; EF Core *ExecuteUpdate*, rows affected), a `CustomerLinks` row is inserted with the login from the cookie's `sub` and the customer from the challenge's `CustomerId`, and `Linked` `Succeeded` is recorded with that `CustomerId`; the transaction commits, then `disputedesk.linking.events` counts `completed`
   **And** the same login then reaches `GET /api/v1/transactions` with no new sign-in, because linked state is read from `CustomerLinks` on every request (AD-10)
   **And** no request field names the challenge or the customer: verification acts only on the login's current challenge (`SupersededAtUtc` and `ConsumedAtUtc` null); a correct code for a challenge superseded by a later start answers like a wrong code on the current one and links nothing
   **And** the comparison is `CryptographicOperations.FixedTimeEquals` over the HMAC-SHA256 of the challenge ID and the submitted code under `Linking:OtpHmacKey` (story 6.1), computed on every attempt; the code is expired at the stored `ExpiresAtUtc` and accepted one microsecond before it (FR-5)

2. **Linking keeps the existing session** (R7; FR-6; AD-7)
   **Given** a login that has just linked in criterion 1
   **When** its next request arrives with the same session cookie
   **Then** the verify response signs nobody out or in and sets no session cookie, the `Sessions` row keeps its `KeyHash`, `LoginSub` and `AbsoluteExpiresAtUtc` unchanged, and that next request reaches `GET /api/v1/transactions` as the linked customer and gets 403 `already-linked` on the linking routes, because linked state is read from `CustomerLinks` on every request (AD-10; human decision, 2026-10-30)
   **And** logout after linking still sends `id_token_hint`, because the ticket is unchanged (AD-7)

3. **Three attempts, then locked** (R7; FR-6, FR-7)
   **Given** customer D's current claimed challenge
   **When** the login posts a wrong code, then a wrong code, then a third code, and in a second run a fourth code
   **Then** the first wrong code gets 422 `otp-incorrect` with `attemptsRemaining` 2, the second 1, a wrong third code gets 409 `linking-locked`, a correct third code links (criterion 1), and any fourth attempt, the correct code included, gets 409 `linking-locked`
   **And** each attempt is one parameterised raw-SQL statement in one named Infrastructure method, run through `Database.SqlQuery<T>` (Microsoft Learn, EF Core *SQL queries*, unmapped types; `ExecuteUpdate` returns only a count, so the builder's report gives the SQL as evidence): `UPDATE "LinkingChallenges" SET "Attempts" = "Attempts" + 1 WHERE "LoginSub" = @sub AND "SupersededAtUtc" IS NULL AND "ConsumedAtUtc" IS NULL AND "Attempts" < 3 RETURNING` the row's ID, `CustomerId`, `OtpHmac`, `ExpiresAtUtc` and `Attempts`
   **And** a counted attempt that does not link answers 422 `otp-incorrect` with `attemptsRemaining` = 3 minus `Attempts` while `Attempts` is under 3, and 409 `linking-locked` when it is 3; when the update returns no row, one read of the login's current challenge answers 409 `linking-locked` if one exists (`Attempts` 3) and the no-row answer of criterion 4 if none, neither counted
   **And** an expired claimed challenge counts the attempt and answers the same way, the correct code included (FR-5)

4. **Every kind of challenge answers like a wrong code; a login with none is never counted** (R5, R7; FR-4, FR-6, FR-7)
   **Given** a blank challenge from each stop of story 6.1 (details that match nothing, customer A's already-linked details, customer C after the failed SIM-swap check), a released challenge (`OtpHmac` null after a failed send), and a login with no challenge row
   **When** each posts codes three times
   **Then** each challenge gives the same status and body (apart from `traceId`) as customer D's challenge given wrong codes, attempt by attempt: 422 with `attemptsRemaining` 2, then 1, then 409 `linking-locked`, comparing against a fixed 32-byte dummy HMAC when `OtpHmac` is null (OWASP Authentication Cheat Sheet, generic responses); customer C ends on `linking-locked` (FR-6)
   **And** the login with no challenge row gets 422 `otp-incorrect` with `attemptsRemaining` 2 every time, never `linking-locked`, and no attempt is counted, because it holds no OTP and can never succeed (human decision, 2026-10-30)

5. **The code's bounds; a malformed code counts nothing** (R7; FR-5, AD-3, AD-8)
   **Given** the one request field: `code`, required, a JSON string of exactly six ASCII digits `0`–`9` (leading zeros allowed), not trimmed; any other field is ignored
   **When** an unlinked login posts a well-formed JSON body whose `code` is missing, null, empty, 5 digits, 7 digits, with a letter, padded with a space, in Arabic-Indic digits (`"١٢٣٤٥٦"`) or 10,000 characters long
   **Then** each gets 400 `validation` naming only `code`, the body never contains the submitted value, and no attempt is counted and no audit row written, through the built-in minimal API validation (`AddValidation` with `[RegularExpression("^[0-9]{6}$")]`, not `\d` or `char.IsDigit`, which accept other Unicode digits; Microsoft Learn, *Minimal APIs validation*)
   **And** a body the framework cannot bind (`code` as a JSON number, a body that is not valid JSON, no body) gets the framework's 400 `validation` with no field named, before any lookup, and likewise counts no attempt and writes no audit row (AD-8; Microsoft Learn, *Parameter binding in minimal APIs*: endpoint filters do not run for body-reading failures)
   **And** checks run 401 (anonymous, bearer only), 403 `already-linked` (a linked login), 400 (missing antiforgery token, then the body), as on the start route (AD-8, AD-7)

6. **Parallel verifications count at most 3 and link once** (R7; FR-6, FR-7, NFR-TEST-3)
   **Given** customer D's current claimed challenge
   **When** the login sends 10 verifications with the correct code in parallel
   **Then** exactly one gets 204 and exactly one `CustomerLinks` row exists, at most 3 attempts are counted (counting stops once the challenge is consumed), the conditional consume lets only one transaction through, and every other response is 422 `otp-incorrect`, 409 `linking-locked` or 403 `already-linked`, never 500

7. **Two logins racing for one customer** (R7; FR-6, NFR-DATA-5)
   **Given** two logins, each with a current claimed challenge for customer D and its correct code
   **When** both verify at once
   **Then** one links; the other's link insert fails on `UQ_CustomerLinks_CustomerId`, its transaction rolls back with its consume, and it gets the counted-attempt answer of criterion 3 (422 `otp-incorrect` with its `attemptsRemaining`, or 409 `linking-locked` when it is its third), never a 500, because `UniqueViolations` maps `UQ_CustomerLinks_CustomerId` and `UQ_CustomerLinks_LoginSub` to a linking error the handler catches (AD-15); the loser records `Linked` `Rejected`, unless it was its third attempt, which locks its challenge: `LinkingLocked` `Rejected` with the customer ID, and `locked` counted once (criterion 8)

8. **Every outcome audited and counted, nothing personal kept** (R10, R31, R36; FR-10, NFR-SEC-5, NFR-OBS-3)
   **Given** each outcome
   **When** it occurs
   **Then** it is recorded through `AuditLog.Record` with the login, the client IP and, where the challenge has one, the `CustomerId`: a counted wrong code is `LinkingOtpIncorrect` `Rejected`; a counted attempt on an expired claimed challenge is `LinkingOtpExpired` `Rejected`; the attempt that locks and every later locked attempt are `LinkingLocked` `Rejected`; a login with no challenge row is `LinkingOtpIncorrect` `Rejected` with no `CustomerId`; a link is `Linked` `Succeeded` inside the link transaction, so a link never commits without it; a race loser is `Linked` `Rejected`, except a two-login loser on its third attempt (criterion 7)
   **And** `disputedesk.linking.events` counts `locked` once per challenge, when it locks, not per 409, and `completed` once per link; a verification that loses the consume to a parallel verification or a later start counts no `locked`; labels carry only the event
   **And** a failed save of a failure entry leaves the response unchanged and logs one error line holding only the action
   **And** the submitted code and its HMAC appear in no table column other than `OtpHmac`, no log and no span; the verify request record seals `ToString` (3.8 Known pitfall), and the privacy test of story 6.1 covers a verification

## Boundaries

- Start, the claim, the send, the cap and the full queue (stories 6.1 and 6.5) do not change; verification only reads and updates the challenge 6.1 writes. The blank a full-queue 429 leaves is proved to verify as a wrong code in story 6.3, which builds after both 6.5 and this story.
- The per-login and per-IP rate limits are story 6.3; until then verify is limited only by the gateway's `limit_req` (AD-23).
- No new repository, session protocol or OTP store: the existing `Unlinked` policy, antiforgery filter, `AuditLog.Record`, `UniqueViolations` and the cookie handler with `SessionTicketStore` (AD-7, AD-12); the session, its key and `SessionTicketStore` do not change.
- The AD-3 slug catalogue does not change: `otp-incorrect` (422, adds `attemptsRemaining`) and `linking-locked` (409) already exist.

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- On the running stack, link customer D through the gateway with the OTP from Mailpit: the browser reaches Transactions without signing in again, and its session cookie value is unchanged.
- In a throwaway copy, remove the two `UQ_CustomerLinks_*` entries from `UniqueViolations`: criterion 7's loser gets a 500 and the test fails.
- In a throwaway copy, drop `"Attempts" < 3` from the attempt statement: criteria 3 and 6 fail.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: M2.
- Build order: after 6.1 merges; it may build beside 6.5, 6.6 and 5.3, and 6.3 waits on it. With 6.5 it shares `Api/Linking/LinkingEndpoints.cs` (6.5 changes the start operation's result and metadata; this story adds the verify operation), `Infrastructure/Linking/` (6.5 changes the job, `OtpSends` and `LinkingOptions`; this story adds the attempt, consume and link statements and their registration) and the generated contract files, all in different sections; the later merge rebases and regenerates `openapi.json` and `frontend/src/api/generated/`. With 6.6 it shares only `Program.cs` (the verify mapping, not the pipeline's start). With 5.3 it shares no file, and `CustomerLinkConfiguration.cs` changes no model, so it adds nothing to `AppDbContextModelSnapshot` (the EF drift check proves it).
- Success response: 204 No Content with no `Set-Cookie`; `openapi.json` names 204, 400, 401, 403, 409 and 422 on the verify operation.
- Touches:
  - `backend/src/DisputeDesk.Contracts/Linking/` (the verify request)
  - `backend/src/DisputeDesk.Application/Linking/LinkingErrors.cs`
  - `backend/src/DisputeDesk.Infrastructure/Linking/` (new: the verify handler, beside 6.1's `LinkingJobSteps`, because `CustomerLink` and `Database.SqlQuery` live in Infrastructure; the attempt statement, the consume, the link insert), `Infrastructure/Persistence/UniqueViolations.cs`, `Infrastructure/Persistence/CustomerLinkConfiguration.cs` (index name constants)
  - `backend/src/DisputeDesk.Api/Linking/` (the endpoint), `Api/Program.cs` (maps verify), `Api/Errors/` (only if `attemptsRemaining` needs it)
  - `backend/openapi.json`, `frontend/src/api/generated/` (regenerated; CI fails on drift)
  - `backend/tests/DisputeDesk.Api.IntegrationTests/Linking/` (new verify tests), `Authorization/EndpointAuthorizationTests.cs` (`Unlinked` on verify)
- No migration: `ConsumedAtUtc` and `Attempts` exist from 6.1.

## References

- parent — docs/tickets/epic-6-linking/epic-6-linking.md
- architecture — docs/architecture/architecture.md, AD-3, AD-7, AD-8, AD-10, AD-12, AD-14a, AD-15, AD-20
- prd — docs/prd/prd.md, FR-4, FR-5, FR-6, FR-7, FR-10, NFR-DATA-5, NFR-SEC-5, NFR-TEST-3
- challenge — docs/verification/6.2-challenge.md
- review — docs/verification/6.2-prebuild-review.md
- security — docs/verification/6.2-prebuild-security.md
- standard — <https://learn.microsoft.com/ef/core/saving/transactions>
- standard — <https://learn.microsoft.com/ef/core/querying/sql-queries>
- standard — <https://learn.microsoft.com/ef/core/saving/execute-insert-update-delete>
- standard — <https://learn.microsoft.com/dotnet/api/system.security.cryptography.cryptographicoperations.fixedtimeequals>
- standard — <https://learn.microsoft.com/aspnet/core/security/authentication/cookie>
- standard — <https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/validation>
- standard — <https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html>
- standard — <https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html>

## Notes

- Risk high: account takeover; beyond the criteria, the security agent times correct and wrong comparisons, and blank and claimed challenges.
- Decision: 2026-10-27 — `otp-incorrect` is 422 (`BusinessRule`, RFC 9110 §15.5.21) and `linking-locked` is 409 (`Conflict`, RFC 9110 §15.5.10); the problem pages state both.
- Decision: 2026-10-30 (human, 6.2 challenge) — FR-7 wins: a correct code on the third attempt links; lockout comes after three wrong codes (AD-12 amended). A blank challenge (no match, already linked, SIM swap failed) answers and counts exactly like a wrong code. A login with no challenge row gets the same wrong-code answer without counting: it holds no OTP, so it can never succeed and learns nothing. It replaces the 2026-10-27 assumption of `attemptsRemaining` 2 and FR-6's "the attempt counts" for a login with no challenge (FR-6 amended) (docs/verification/6.2-challenge.md, Human decisions 1).
- Decision: 2026-10-30 (human, 6.2 challenge) — link first, then re-issue: consume the challenge and insert the link in one EF Core transaction; after it commits, sign out and sign in on the cookie scheme carrying the ticket's properties, so the `id_token` survives. If the re-issue fails, the link stands, the old session keeps working and the failure is logged (AD-12's "commit together" amended) (docs/verification/6.2-challenge.md, Human decisions 2).
- Decision: 2026-10-30 (Sofia, 6.2 pre-build reviews) — success is 204; the race loser answers like a counted wrong attempt and records `Linked` `Rejected`; a locked attempt after the lock is audited `LinkingLocked` but counted in the metric only once.
- Decision: 2026-10-30 (Sofia, 6.5 pre-build review) — the check that a full-queue blank verifies as a wrong code moved here from 6.5 (criterion 4).
- Decision: 2026-10-30 (human, after this refine) — option (A): linking keeps the existing session key; there is no sign-out or sign-in, and AD-7's renewal of the key after linking is dropped. The key is minted server-side at sign-in and the ticket store rejects any key it did not mint, so session fixation is already blocked. The decided sign-out then sign-in could not work: in ASP.NET Core's `release/10.0` source, `HandleSignOutAsync` leaves the handler's `_sessionKey` set, so a later `SignInAsync` in the same request renews the removed key and the next request gets 401. It replaces the re-issue half of the decision above; the link-first half stands. Renewing the key is a production-path line (story 7.6) (docs/verification/6.2-challenge.md, Resolution).
- Resolution: 2026-10-30 (Sofia, parallel re-slice) — the only part of this story that needed 6.5 was the full-queue blank in criterion 4; that case moved to story 6.3 (criterion 4), which waits on both, so this story now waits only on 6.1 and builds beside 6.5. The released challenge stays here: 6.1's release already clears `OtpHmac`, and verification never reads `OtpSentAtUtc`, which 6.5 keeps. No criterion was added, and no PRD requirement or architecture decision changed. It supersedes the 6.5 pre-build-review line above: the full-queue check now lives in 6.3.
- Handoff (2026-10-30, 6.1 refine): verification reads the challenge 6.1 writes: the login's current row (`SupersededAtUtc` and `ConsumedAtUtc` null), `OtpHmac`, `ExpiresAtUtc` and `Attempts`; from 6.5 a released row keeps `OtpSentAtUtc` and has `OtpHmac` null, and verifies as a wrong code.
- Assumption: 2026-10-30 (Sofia) — a malformed code (400) counts no attempt: it never reaches the handler, so it cannot spend a try (EXPERIENCE.md: a mistyped digit never spends one of the 3 attempts).
- Resolution: 2026-10-30 (Sofia, after the 6.2 verify and review) — wording aligned with decisions already made, no PRD requirement or architecture decision changed: criterion 6's "`Attempts` is exactly 3" becomes "at most 3 counted attempts and exactly one link", the property FR-7 needs, since counting stops once the challenge is consumed (the test may still gate the consumes to make exactly 3 deterministic) (docs/verification/6.2-verify.md, Supervisor disposition); criterion 5's "naming only `code`" applies to a well-formed body whose `code` breaks its bounds, while a body the framework cannot bind gets the framework's 400 `validation` with no field named, as 3.4, 5.2 and 6.1 do (AD-8; docs/verification/6.2-verify.md, Supervisor decision on AC5); the Touches list puts the handler in `Infrastructure/Linking/` (docs/verification/6.2-review.md, Handler location). Criterion 7 (the race loser's audit action) is unchanged while security judges it.
- Resolution: 2026-10-30 (Sofia, after the 6.2 security re-run) — wording aligned with the human decision that criterion 8 wins over criterion 7, no PRD requirement or architecture decision changed: a two-login race loser gets criterion 3's counted-attempt answer, 409 `linking-locked` when it is its third, and on that third attempt records `LinkingLocked` `Rejected` with the customer ID and counts `locked` once, not `Linked` `Rejected`; a verification that loses the consume to a parallel verification or a later start counts no `locked` (docs/verification/6.2-security.md, fix round 1 re-run, AC7; docs/verification/6.2-fixes-2.md; docs/verification/6.2-verify.md). It settles the line above that left criterion 7 open while security judged it.
