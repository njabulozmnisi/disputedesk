---
title: DisputeDesk
status: final
created: 2026-10-25
updated: 2026-10-26
inputs:
  - docs/product-brief/product-brief.md
  - docs/glossary.md
  - docs/research/dispute-lifecycle/dispute-lifecycle.md
  - docs/research/event-versioning/event-versioning.md
---

# PRD: DisputeDesk

## 0. Document purpose

This PRD serves the downstream planning skills (Sofia, UX, architecture) and the builder, verifier, QA and reviewer agents. It turns the product brief (`docs/product-brief/product-brief.md`) into requirements that can be tested. The brief's settled technical constraints, identity design and seed data are folded into this PRD: the features in §4 (seed detail in FR-29) and the non-functional requirements in §5.

Conventions:

- Vocabulary comes from `docs/glossary.md`, which §3 repeats and extends.
- Features are grouped, with globally numbered FRs nested under each one. FR IDs are stable, so a gap in the numbering is never reused.
- Non-functional requirements sit in §5 under the ten quality areas, with IDs `NFR-<AREA>-<n>`.
- Unconfirmed inferences are tagged `[ASSUMPTION]` inline and indexed in §10.
- Consequences that cannot be automated are tagged `[manual check]`.

Two research reports calibrate the rules. `docs/research/dispute-lifecycle/dispute-lifecycle.md` covers the dispute window, the SLA framing, the reasons and statuses customers see, and POPIA. `docs/research/event-versioning/event-versioning.md` covers the event envelope, versioning, trace propagation and consumer dedupe. Mechanism detail and rationale live in `docs/architecture/architecture.md`.

## 1. Vision

DisputeDesk lets a customer of a South African retail bank see their recent transactions, dispute one that is wrong, and follow that dispute to its outcome without phoning anyone. The bank's operations tools and automated processes move each dispute forward through one API under one set of rules, and each change reaches the rest of the bank as an event.

It meets this business requirement: "The bank requires a customer-facing web application, with the back-end services that support it, through which customers can view their account transactions and raise a dispute against any transaction they believe is incorrect. Customers must also be able to view the history of their disputed transactions, including the current status of each dispute." It is built to production standards and must meet ten quality areas. Security is primary and testing is important. Over-engineering is the standing risk, so each area is met once, done well. Anything bigger is a note for `docs/production-path.md`, not a requirement.

## 2. Who it serves

### 2.1 Jobs to be done

- **Customer:** When a card transaction is unrecognised or wrong, the customer wants to dispute it in a couple of minutes and then see where it stands, without phoning the bank. They also want to be confident that nobody else can see their transactions or disputes.
- **Operations tool or automation (actor):** When a dispute arrives, the actor needs to read it, start review on it, escalate it or resolve it through a stable, authorised API, and have every change audited and published.
- **Engineer new to the solution (maintainer):** They need to see within minutes how each quality area is met, run it with one command, and trust the tests.

### 2.2 Non-users (v1)

- Bank staff who need a case-management UI. Scalar stands in for another team's operations tool.
- Merchants and card schemes.
- Customers who bank only in branches.

There is no admin console.

### 2.3 Information architecture

There are six screens:

1. Sign in or sign up, hosted by the identity provider and themed.
2. Link your login.
3. Transactions.
4. Raise a dispute.
5. Dispute history.
6. Dispute detail with its timeline.

A signed-out visitor lands on a minimal start page with a sign-in button [ASSUMPTION].

### 2.4 Key journeys

Customers see two actor labels on the timeline: "You" for the customer and "The bank" for operations tools and automated processes alike, so an automatic escalation does not reveal the internal SLA.

- **UJ-1. Thandi signs up, links and disputes an unrecognised transaction.**
  - **Persona and context:** Thandi, a teacher in Durban, sees an R849.00 card debit from a merchant Thandi has never heard of, and has never used DisputeDesk.
  - **Entry state:** anonymous, on the start page.
  - **Path:**
    1. Thandi signs up on the identity provider's hosted, DisputeDesk-themed page and verifies the email address from the inbox (Mailpit in the demo).
    2. Back in DisputeDesk the login is not linked, so only the linking screen is reachable. Thandi enters an ID number and an account number.
    3. Thandi sees a neutral message: "If these details match our records, we've sent a code to the cellphone number we hold for you."
    4. Thandi enters the 6-digit OTP, and the login becomes a linked customer.
    5. Thandi lands on Transactions, newest first, and opens the R849.00 row, chooses Dispute, picks the reason "unrecognised", adds a short note and submits.
  - **Climax:** the transaction row shows the badge "Submitted". Thandi's dispute history lists the dispute, and its timeline has one entry: Submitted, the time in South African time, actor "You".
  - **Resolution:** Thandi signs out, which ends both the DisputeDesk session and the identity provider session.
  - **Edge case:** a pending transaction shows as not disputable and has no Dispute control.
  - **Realised by:** FR-1 to FR-17 and FR-27.

- **UJ-2. Sipho withdraws a dispute that is no longer needed.**
  - **Persona and context:** Sipho, a linked customer, raised a "not received" dispute. Then the parcel arrives.
  - **Path:** Sipho opens the dispute detail (status Under review), chooses Withdraw, adds the optional note "Parcel arrived", and confirms.
  - **Resolution:** the timeline shows Withdrawn, actor "You". The transaction can never be disputed again.
  - **Realised by:** FR-17 to FR-20 and FR-28.

- **UJ-3. An operations tool reviews and resolves a dispute.**
  - **Context:** an operations tool (Scalar in the demo) holds a client-credentials token with the roles `dispute.read`, `dispute.startReview` and `dispute.resolve`.
  - **Path:**
    1. It reads the dispute named in a `raised` event.
    2. It calls start review.
    3. Later it calls resolve with the outcome Upheld and the customer-visible note "We've reversed this debit."
  - **Resolution:** the customer's timeline shows Under review, then Resolved (Upheld) with the note, and the actor shown as the bank. The tool's identity never appears. Each action publishes `disputedesk.dispute.status-changed.v1`.
  - **Realised by:** FR-21, FR-23 to FR-25, FR-28 and FR-30.

- **UJ-4. The Worker escalates a dispute on the SLA.**
  - **Context:** a dispute is still Under review 10 calendar days after it was submitted.
  - **Path:** the Worker's SLA sweep escalates it and publishes the status-changed event.
  - **Resolution:** the customer sees "Under review (prioritised)" and is never told about an SLA.
  - **Realised by:** FR-26 and FR-28.

- **UJ-5. Unhappy paths.**
  - **(a) Another customer's dispute.** Customer A requests a dispute ID that belongs to customer B and gets 404, exactly as if the ID did not exist. Realised by FR-17 and NFR-SEC-6.
  - **(b) A resolved dispute cannot be withdrawn.** The Withdraw control is not offered, and a direct API call returns 409. Realised by FR-20 and FR-24.
  - **(c) Wrong or expired OTP.** Three wrong codes lock the OTP, and the login must start linking again, within the rate limits. A code entered after 5 minutes is rejected. Both cases are audited. Realised by FR-5, FR-7, FR-8 and FR-10.
  - **(d) An unlinked login calls a customer endpoint.** `GET /api/v1/transactions` returns 403 with a problem type that sends the SPA to linking. Realised by FR-9.
  - **(e) Second dispute on a transaction.** A customer tries to dispute a transaction whose earlier dispute was Rejected and gets 409. Realised by FR-13.

## 3. Glossary

The terms from `docs/glossary.md` are used verbatim. No synonyms appear anywhere in this PRD or downstream.

- **Customer:** a person the bank holds an account for, identified by the bank's customer ID.
- **Login:** a person's sign-in identity with the identity provider. A login is not a customer until it is linked.
- **Linked customer:** a login that has proved it belongs to a customer and is linked to that customer's ID.
- **Account:** a bank account that holds transactions.
- **Transaction:** a settled or pending movement on an account, read from the bank's core banking system.
- **Disputable transaction:** a settled card debit made within the last 90 days that has never been disputed.
- **Dispute:** a customer's claim that a transaction was wrong. It is raised for the full amount, or for the difference when the reason is wrong amount. One per transaction, ever.
- **Reason:** why the customer disputes. One of unrecognised, duplicate charge, wrong amount, not received, cancelled subscription, refund not processed. Refund not processed follows the dispute-lifecycle research (recommendation 6). ATM cash not dispensed is not a reason: an ATM withdrawal is not a card debit to a merchant in this product.
- **Status:** where a dispute is: Submitted, Under review, Escalated, Resolved or Withdrawn. Customers see Escalated as "Under review (prioritised)".
- **Outcome:** the result of a resolved dispute: Upheld or Rejected.
- **Action:** a named change to a dispute's status: start review, escalate, resolve, withdraw.
- **Actor:** whoever performed an action: the customer, an operations tool, or an automated process. It is recorded and never used to decide anything.
- **SLA:** the 10 calendar days from submission after which an unresolved dispute is escalated automatically. It is internal only.
- **Timeline:** the ordered history of a dispute's status changes, with notes meant for the customer. The internal audit log is separate.

This PRD adds:

- **Actor type:** the stored category of an actor: `customer`, `operations` or `automation`.
- **Linking:** the process by which a login proves it belongs to a customer. One login per customer, and one customer per login.
- **OTP:** the 6-digit one-time code sent during linking.
- **Authorisation state:** anonymous, authenticated but not linked, or linked.
- **Role:** a permission on an actor's API credential: `dispute.read`, `dispute.startReview`, `dispute.escalate` or `dispute.resolve`. Role keys are camelCase after the `dispute.` prefix. Settled on 2026-10-26.
- **Customer endpoint:** an endpoint called by the SPA with the BFF cookie. Withdraw is one.
- **Operations endpoint:** an endpoint called with a bearer token: start review, escalate, resolve, and reading a dispute.
- **Worker:** the background process that relays dispute events and runs the SLA sweep.
- **Dispute event:** a fact published to Kafka when a dispute is raised or its status changes.
- **Transaction date:** the date the core-banking source gives as the date the transaction was made (the authorisation date), not the posting date. It stands in for the scheme's processing date (dispute-lifecycle research, section 1).

The six technical terms above (actor type, authorisation state, role, customer endpoint, operations endpoint, Worker) stay in this PRD only and are not added to `docs/glossary.md`. Settled on 2026-10-26.

## 4. Features

### 4.1 Sign-in and session

**Description:** authentication is the identity provider's job. Authorisation is DisputeDesk's job, enforced in the API. Zitadel, self-hosted, stands in for the bank's identity platform, and the README says so plainly. The DisputeDesk API host acts as a backend-for-frontend (BFF), so the browser never holds a token. Realises UJ-1.

#### FR-1: Sign up and verify email

A person can create a login and verify their email on the identity provider's hosted pages, themed to match DisputeDesk. Realises UJ-1.

**Consequences (testable):**

- The verification email and the password-reset email arrive in Mailpit.
- An unverified login cannot complete sign-in to DisputeDesk.
- The hosted pages use DisputeDesk's design tokens: colours, type and logo [manual check].

#### FR-2: Sign in through the BFF

A person with a verified login can sign in. DisputeDesk then holds their session server-side and gives the browser only an HttpOnly, SameSite=Strict session cookie, Secure with the `__Host-` prefix in AWS (RFC 6265bis; local `http://localhost` is exempt). Realises UJ-1.

**Consequences (testable):**

- Sign-in uses the authorisation code flow with PKCE, with the API host as the confidential client (RFC 9700; OAuth 2.0 for Browser-Based Applications, BFF pattern).
- No access, ID or refresh token is ever readable by browser JavaScript or present in any browser-visible response.
- SameSite=Strict applies to the session cookie only. The handler's short-lived correlation and nonce cookies use the handler defaults, so the cross-site callback works.
- An E2E test signs in successfully through the real identity provider in Compose.
- The session expires after 15 minutes of inactivity and after 12 hours in total. The SPA then sends the person to sign in again [ASSUMPTION].
- At least 2 minutes before the idle sign-out, the SPA warns in a dialog, and "Stay signed in" renews the session (WCAG 2.2 SC 2.2.1 Timing Adjustable). Settled on 2026-10-27.

#### FR-3: Sign out

A signed-in person can sign out. Signing out ends both the DisputeDesk session and the identity provider session, through OpenID Connect RP-Initiated Logout 1.0. Realises UJ-1.

**Consequences (testable):**

- The server-side session record is deleted, so a copy of the old cookie replayed afterwards gets 401.
- Signing in again requires credentials at the identity provider.

### 4.2 Linking a login to a customer

**Description:** a new login proves it belongs to a customer with something it knows (ID number and account number) and something it holds (the cellphone on the bank's record). Every response is shaped so that nobody can use linking to learn whether a person banks here. Realises UJ-1 and UJ-5c.

#### FR-4: Start linking

An authenticated, unlinked login can submit an ID number and an account number. DisputeDesk looks them up through `ICoreBankingClient`. Realises UJ-1.

**Consequences (testable):**

- The response is identical in status and body in all three cases (OWASP Authentication Cheat Sheet, generic responses): the details match a customer, they match nothing, or they match a customer who already has a linked login.
- Sending the OTP happens off the request path, so response time does not depend on whether the details matched. A test asserts that no SMS call is made inside the request.
- Only when the details match a customer with no linked login is a challenge created and an OTP queued. The challenge records the customer ID resolved at this step.
- Before any OTP is sent, the SIM-swap check (`ISimSwapCheck`) runs for the cellphone on the bank's record. If it fails, errors or times out, no OTP is sent and no challenge can succeed: this fails closed. The response is unchanged (same status and body), so nothing reveals the result, and the failure is audited. Seeded customer C fails this check.
- The OTP goes to the cellphone number on the bank's record, through `ISmsSender` (which writes to Mailpit). A number the person types is never used.
- At most 3 OTPs are sent to one customer in any hour, across all logins and IP addresses. Requests beyond that get the same response, send nothing, and are audited [ASSUMPTION].
- The ID number is never persisted (database, outbox, events, idempotency store) and never logged, traced or put in metrics. Request-body capture is turned off on linking routes.
- A malformed ID number (not 13 digits, or a failed checksum) or a malformed account number returns 400. The response names the field but never echoes its value, and does not reveal whether anything matched [ASSUMPTION].
- Starting linking again invalidates the login's earlier challenge.

#### FR-5: OTP rules

An OTP is 6 digits, stored only as an HMAC hash, valid for 5 minutes, and usable once. Realises UJ-1 and UJ-5c.

**Consequences (testable):**

- The database holds only the HMAC, never the plain code.
- A code is expired when the current time is at or after the issue time plus 5 minutes, and a correct code then is rejected.
- A used code is rejected.

#### FR-6: Verify OTP and link

An unlinked login can enter an OTP. If the code is correct, the login becomes a linked customer (the SIM-swap check already ran before the OTP was sent, FR-4). Realises UJ-1.

**Consequences (testable):**

- Once linked, the login can reach the customer endpoints at once, with no new sign-in.
- When the login has no active challenge (because the details didn't match, the customer was already linked, or the challenge was invalidated), verification responds exactly like a wrong code. The attempt counts towards FR-7 and FR-8.
- The attempt counter and the used flag change in one atomic conditional update before the code is compared. In a test, parallel verifications never yield more than 3 attempts or more than one success.
- A login whose SIM-swap check failed received no OTP; any code it enters responds exactly like a wrong code (FR-7 applies). The copy after the attempts run out is "We can't link you online. Please contact the bank."
- The link table has unique constraints on customer ID and on login. If two logins race to link one customer, the loser gets the same generic failure.

#### FR-7: Lockout after three failures

After 3 wrong codes the challenge is locked. Realises UJ-5c.

**Consequences (testable):**

- The 4th attempt fails, even with the correct code.
- The login must start linking again, which is subject to FR-8.

#### FR-8: Linking rate limits

Linking attempts, start and verify combined, are limited to 5 per login and 20 per client IP address in any 15 minutes, using the built-in rate limiter. Realises UJ-5c.

**Consequences (testable):**

- A login's 6th attempt within 15 minutes returns 429 (RFC 6585) with `Retry-After` (RFC 9110).
- An IP address's 21st attempt within 15 minutes returns 429, whichever logins are used.
- Requests rejected with 429 do not count as attempts.
- The client IP is read from forwarded headers only when they come from the known proxies (nginx, and CloudFront in AWS). Otherwise the connection address is used. A test shows that a spoofed `X-Forwarded-For` from an unknown source is ignored.
- Limits are held in memory on the single Api instance. This is stated in the README, and shared limits for scale-out are a production-path note.

#### FR-9: Authorisation states

Every endpoint enforces one of three authorisation states: anonymous; authenticated but not linked (linking endpoints only); or linked (the customer's own transactions and disputes). Realises UJ-5d.

**Consequences (testable):**

- An anonymous request to any customer or linking endpoint returns 401.
- An unlinked login calling a customer endpoint returns 403, with a problem type the SPA treats as "linking required".
- A linked login calling a linking endpoint returns 403, with a problem type the SPA treats as "already linked" and uses to redirect to Transactions [ASSUMPTION].
- On every request the customer is resolved from the identity link table, keyed by login. It is never taken from a request field and never read directly from the token subject.

#### FR-10: Linking audit

Every link and every linking failure is written to the internal audit log with time, login, outcome and client IP address. The ID number is never written. Failures include no match, wrong OTP, expired OTP, lockout, SIM-swap failure, the per-customer send cap, and rate limiting. Realises UJ-5c.

**Consequences (testable):**

- For rate limiting, only the first rejection per login or IP address in each window is audited. Later rejections are counted as a metric.
- v1 has no unlink endpoint. Unlinking, and recovery for a customer who has lost their login, are done with the bank's help and recorded in `docs/production-path.md`. Any unlink added later must be audited [ASSUMPTION].

### 4.3 Transactions

**Description:** a linked customer sees about 90 days of transactions on their one or two accounts, read from a stubbed core-banking source, newest first. Each row shows whether it is disputable, and disputed rows show their status badge. Realises UJ-1.

#### FR-11: View own transactions

A linked customer can list their own transactions, newest first, with keyset pagination. There are no filters and no search. Realises UJ-1.

**Consequences (testable):**

- The list comes from `ICoreBankingClient`, scoped to the accounts the core-banking source reports for the resolved customer. In v1, an account belongs to exactly one customer [ASSUMPTION].
- Each row shows:
  - the transaction date in South African time;
  - the merchant or description;
  - the amount in ZAR with its currency code;
  - whether it is settled or pending;
  - the account, shown by its last 4 digits;
  - the masked card number, where there is one.
- Page size is the integer query parameter `limit`, from 1 to 50, default 20. A `limit` that is not an integer or is outside 1–50, or an invalid or tampered cursor, returns 400 [ASSUMPTION].
- Over a stable data set, paging returns each transaction exactly once, ordered by transaction date and then transaction ID.
- If the core-banking source is still unavailable after the resilience policies, the endpoint returns 503 ProblemDetails and the SPA offers a retry. Dispute history (FR-16) stays available.

#### FR-12: Disputability and status on each row

Each transaction row shows either that it is disputable or the status badge of its dispute. Realises UJ-1 and UJ-5e.

**Consequences (testable):**

- These transactions are not disputable:
  - pending transactions;
  - credits;
  - transfers between the customer's own accounts;
  - debits that are not card payments, such as fees and payments to someone else, because a dispute becomes a card-scheme chargeback (glossary, "Disputable transaction"; decided 2026-10-28);
  - transactions whose transaction date is more than 90 days ago.
- A transaction with a dispute in any status, including Withdrawn and Resolved, shows that dispute's customer-facing status (FR-17) and is not disputable.
- The 90 days are counted in South African calendar days. A transaction dated D is disputable until the end of day D + 90, inclusive [ASSUMPTION]. Boundary tests cover D + 90 and D + 91.

### 4.4 Raising a dispute

**Description:** for a disputable transaction, the customer picks a reason, can add a note, and (for wrong amount only) enters the amount they expected. Raising writes the dispute, its first timeline entry, an audit entry and a `disputedesk.dispute.raised.v1` event in one transaction. Realises UJ-1.

#### FR-13: Raise a dispute

A linked customer can raise a dispute on their own disputable transaction, giving a reason and an optional note of up to 1,000 characters. Realises UJ-1 and UJ-5e.

**Consequences (testable):**

- The request body holds only the transaction ID, the reason, the note and, for wrong amount, the expected amount.
- The server reads the transaction from `ICoreBankingClient`, checks it belongs to one of the customer's accounts, decides at that moment whether it is disputable, and stores a snapshot of it on the dispute. The snapshot holds date, description, amount, currency, masked card number and the account's last 4 digits. Any amount, date or description in the request is ignored.
- On success the API returns 201 with the dispute and a `Location` header naming it (RFC 9110). Its status is Submitted, and its timeline has one entry: Submitted, actor type `customer`, with the customer's note.
- If the transaction has ever been disputed, the request returns 409, whatever the earlier dispute's status or outcome, including Rejected and Withdrawn. A unique index on (account reference, transaction ID) enforces this under concurrent requests.
- Any other non-disputable transaction (FR-12) returns 422 [ASSUMPTION]. A row the list showed as disputable can return 422 if the window closes before the POST arrives. The SPA shows the reason and refreshes the row.
- Another customer's transaction, or one that does not exist, returns 404.
- An unknown reason, or a note longer than 1,000 characters, returns 400 with field errors. Notes are trimmed, and length is counted in characters after trimming. A note that is empty or only whitespace is stored as no note.
- If the core-banking source is unavailable, the request returns 503. No dispute or event is written, and no idempotent response is stored.
- The raise is audited, with the login as the actor identity.

#### FR-14: Wrong amount

When the reason is wrong amount, the customer must enter the amount they expected. The dispute is for the transaction amount minus the expected amount. Realises UJ-1.

**Consequences (testable):**

- An expected amount is required for wrong amount and rejected with 400 for every other reason.
- Amounts are the absolute ZAR amount the customer was billed. For a foreign-currency card debit, that is the ZAR billed amount, not the original currency.
- The expected amount must be R0.00 or more, less than the transaction amount, and have no more than 2 decimal places. Otherwise the request returns 400 [ASSUMPTION]. The disputed amount is therefore always more than zero.
- Disputed amounts are stored as `numeric(19,2)` with ISO currency code ZAR. They travel as decimal strings in the API, and floating-point arithmetic is never used.

#### FR-15: Idempotent submission

The POST that raises a dispute requires an `Idempotency-Key` header, following the IETF Idempotency-Key header draft (draft-ietf-httpapi-idempotency-key-header), whose status codes the consequences below use. Realises UJ-1.

**Consequences (testable):**

- A missing key, or a key outside 1–255 printable characters, returns 400 [ASSUMPTION].
- The key is an RFC 9651 Structured Field String, as the draft defines it, so the value is quoted (`Idempotency-Key: "<uuid>"`); any other form returns 400. Settled on 2026-10-27.
- Replaying a key with the same validated request returns the original response, with no second dispute and no second event. "Same" means the same hash of the canonical validated request.
- Reusing a key with a different request returns 422.
- A request whose key is still being processed returns 409 with an "in progress" problem type. The key row is inserted first, under a unique constraint.
- Only 201 responses and deterministic 4xx responses are stored. On a 5xx the key is released.
- Keys are scoped to the customer and kept for 24 hours [ASSUMPTION].

### 4.5 Dispute history and timeline

**Description:** this is the historic view the brief requires. A linked customer lists every dispute they have raised and opens one to see its timeline. Realises UJ-1 to UJ-4.

#### FR-16: Dispute history list

A linked customer can list their disputes, newest first, with keyset pagination. Each row shows the transaction summary from the snapshot, the reason, the disputed amount, the customer-facing status, and the outcome when the dispute is Resolved. Realises UJ-1.

**Consequences (testable):**

- Customer A's seed data shows a dispute in every status.
- Only the customer's own disputes appear.
- The list works while the core-banking source is down.

#### FR-17: Dispute detail and timeline

A linked customer can open one of their disputes and see its details and its timeline, oldest entry first. Realises UJ-1 to UJ-4 and UJ-5a.

**Consequences (testable):**

- Each timeline entry shows:
  - the new status as the customer sees it;
  - the time in South African time (stored in UTC);
  - the actor type;
  - the note meant for the customer, if there is one.
- Escalated appears as "Under review (prioritised)" in both the list and the detail.
- No customer response or screen contains the actor's identity, an SLA date, a due date, or a promised resolution time.
- When the outcome is Rejected, the detail tells the customer they can complain to the bank and, after that, to the National Financial Ombud Scheme. This follows the Conduct Standard's duty to explain the process (dispute-lifecycle research, section 1) [ASSUMPTION: static copy only].
- A request for another customer's dispute and a request for an ID that does not exist both return 404 with bodies that are identical apart from `traceId`.

### 4.6 Customer withdrawal

#### FR-18: Withdraw own dispute

The customer who owns a dispute can withdraw it from Submitted, Under review or Escalated. They can add an optional note of up to 500 characters [ASSUMPTION: 500, matching the resolve note]. Realises UJ-2.

**Consequences (testable):**

- The status becomes Withdrawn.
- A timeline entry is added with actor type `customer` and the note.
- An audit entry is added and a status-changed event is published.
- Withdraw is a customer endpoint. It needs the BFF cookie and the antiforgery header, and a bearer token cannot withdraw.
- If a retried withdraw gets 409 and the dispute's current status is Withdrawn, the SPA treats that as success.

#### FR-19: Withdrawn is final

A Withdrawn dispute accepts no further actions, and its transaction is never disputable again. Realises UJ-2.

#### FR-20: Cannot withdraw a closed dispute

Withdrawing a Resolved or Withdrawn dispute returns 409, and the SPA does not offer Withdraw for those statuses. Realises UJ-5b.

### 4.7 Actions by operations tools and automation

**Description:** an action works the same whoever performs it. Any authorised actor calls the same operations endpoints under the same rules, whether that is a person using an operations tool, an LLM agent or an automation. There is one role per action. Actor type and identity are recorded for audit and never used to decide anything. Realises UJ-3.

#### FR-21: Start review

An actor with the role `dispute.startReview` can move a Submitted dispute to Under review. Realises UJ-3.

#### FR-22: Escalate

An actor with the role `dispute.escalate` can move a dispute from Submitted or Under review to Escalated. Realises UJ-3.

#### FR-23: Resolve

An actor with the role `dispute.resolve` can move a dispute from Under review or Escalated to Resolved. The actor must give an outcome (Upheld or Rejected) and a customer-visible note of 1 to 500 characters after trimming. Realises UJ-3.

**Consequences (testable):**

- A missing outcome, or a note that is missing, only whitespace, or longer than 500 characters, returns 400.
- The outcome and the note appear on the customer's timeline.

#### FR-24: Allowed actions

Only the actions in this table are allowed. Any other action on a dispute returns 409, with the dispute's current status in the ProblemDetails. Realises UJ-3 and UJ-5b.

| From \ Action | start review | escalate | resolve | withdraw (owning customer) |
|---|---|---|---|---|
| Submitted | Under review | Escalated | 409 | Withdrawn |
| Under review | 409 | Escalated | Resolved | Withdrawn |
| Escalated | 409 | 409 | Resolved | Withdrawn |
| Resolved | 409 | 409 | 409 | 409 |
| Withdrawn | 409 | 409 | 409 | 409 |

**Consequences (testable):**

- Start review and escalate take no note [ASSUMPTION]. Their timeline entries show only the status and the actor type.
- Each dispute carries a version. When two actions race, the version check lets exactly one succeed, and the other returns 409.
- Repeating an action that already succeeded returns 409. Operations actions are not idempotency-keyed [ASSUMPTION].
- Checks run in this order: 401, 403, 404, 400, 409. A caller without the role therefore cannot probe which dispute IDs exist.

#### FR-25: Operations authentication and audit

Operations endpoints accept only bearer JWTs issued through client credentials. Each action writes an audit entry. Realises UJ-3.

**Consequences (testable):**

- A BFF cookie on an operations endpoint returns 401.
- A valid token without the endpoint's role returns 403.
- A credential that does not map to exactly one known actor type returns 403. The mapping is one Zitadel service account per actor type [ASSUMPTION].
- An action on a dispute ID that does not exist returns 404.
- The audit entry records the time, dispute ID, action, from-status, to-status, actor type, actor identity (the service account's ID) and trace ID.
- A rejected operations attempt after authentication (403, 404 or 409) is also audited, with its actor, action and result.

#### FR-30: Read a dispute (operations)

An actor with the role `dispute.read` can read one dispute by ID: snapshot, reason, amount, status, outcome and timeline. Realises UJ-3.

**Consequences (testable):**

- The endpoint is bearer-only, and a BFF cookie returns 401.
- An unknown ID returns 404.
- The customer's personal details are not returned beyond what the snapshot holds.
- Listing and search for operations are out of scope. Operations tools find disputes from events [ASSUMPTION].

### 4.8 SLA escalation

#### FR-26: Automatic escalation

The Worker escalates every dispute that is still Submitted or Under review once the current time is at or after its submission time plus 240 hours (10 calendar days) [ASSUMPTION: 240 hours, not calendar dates]. Realises UJ-4.

**Consequences (testable):**

- The escalation goes through the same domain operation as FR-22. Its actor type is `automation` and its actor identity is `worker:sla-sweep`. The status change, timeline entry, audit entry and event commit atomically.
- The sweep runs at least once a minute [ASSUMPTION]. It claims due disputes in bounded batches with `FOR UPDATE SKIP LOCKED`, so a second Worker cannot double-escalate. After downtime it catches up.
- Disputes that are already Escalated, Resolved or Withdrawn are left alone. A dispute escalated by hand before the SLA is not escalated again.
- If the sweep loses a version race, it reads the dispute again. If the dispute is still Submitted or Under review and past the SLA, it escalates. Otherwise it skips. The timeline never shows a duplicate entry.
- SLA time comes from `TimeProvider`, so tests can move the clock.

### 4.9 Dispute events

**Description:** every dispute change reaches the rest of the bank as an event, following `docs/research/event-versioning/event-versioning.md`. There is no consumer in this repo. The contract is written for consumers that dedupe on the event's `source` and `id` (CloudEvents 1.0). Realises UJ-1 to UJ-4.

#### FR-27: Dispute raised event

Raising a dispute publishes `disputedesk.dispute.raised.v1`. Realises UJ-1.

#### FR-28: Status changed event

Every successful action, including withdrawal and SLA escalation, publishes `disputedesk.dispute.status-changed.v1`. The event carries the dispute ID, from-status, to-status, action, actor type, the outcome when resolving, the time and the dispute version. Realises UJ-2, UJ-3 and UJ-4.

**Consequences (testable, both events):**

- Format and transport:
  - Events are CloudEvents 1.0 structured JSON (`content-type: application/cloudevents+json`).
  - They go on one topic, keyed by dispute ID.
  - `traceparent` travels in the Kafka headers (W3C Trace Context; OpenTelemetry messaging semantic conventions).
- Each event is written to the outbox in the same database transaction as the change it describes. If that transaction rolls back, nothing is published.
- Events carry no personal data: no names, ID numbers, account or card numbers, amounts, notes, customer ID or actor identity [ASSUMPTION: customer ID and amount are also left out; consumers read detail through FR-30].
- Delivery is at least once. The event ID is set when the outbox row is written and stays the same across redelivery. Events for one dispute are published in the order they were committed.
- Enum values (status, action, reason, outcome, actor type) use one wire casing, camelCase (for example `underReview`, `startReview`, `duplicateCharge`), in both the API and the events, serialised by `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`. C# enum members are PascalCase, and Postgres stores the member names. Settled on 2026-10-26.
- Each type has a committed JSON Schema. A test checks serialised samples against it, and checks that v1 sample JSON still deserialises.

### 4.10 Demo seed data

#### FR-29: Seeded customers

`fixtures/` seeds four customers on the stubbed core-banking source. Each has about 90 days of transactions on one or two accounts, including one pending transaction and one older than 90 days. All dates are relative to start-up, so the data never goes stale.

**Consequences (testable):**

- Customer A is linked and has a dispute in every status, including Resolved with each outcome. A's Submitted and Under review disputes are less than 10 days old, so the sweep does not escalate them on start-up. One Under review dispute is seeded to pass the SLA a few minutes after start-up, so that UJ-4 can be demonstrated [ASSUMPTION].
- Customer B is linked and owns the dispute that customer A gets 404 for.
- Customer C is not linked and fails the SIM-swap check.
- Customer D is not linked and passes every check. D is the customer who completes linking in UJ-1 and its E2E test [ASSUMPTION: added so that linking can be demonstrated]. Each E2E (QA) run starts on a fresh stack (`docker compose down -v`, then `docker compose up --wait`), which resets D's link; no test-only database role is used. Settled on 2026-10-26.
- The README lists the demo logins for A and B and the synthetic ID and account numbers for C and D.
- Seeded disputes publish no events [ASSUMPTION].
- The seeder changes a seeded dispute's status only through the same domain actions as FR-21 to FR-24 and FR-18, with a fixed seeding clock; it never sets a status directly. Settled on 2026-10-26.
- A fresh `docker compose up` produces all of this with no manual steps.

## 5. Non-functional requirements

Each NFR names a threshold or a check. Anything beyond these is a note for `docs/production-path.md`.

### 5.1 Architecture and scalability

- **NFR-ARC-1:** the backend is .NET 10 minimal APIs in Clean Architecture. Domain and application code reference no infrastructure, and an architecture test enforces this.
- **NFR-ARC-2:** the runtime units are as follows. Nothing else is added.
  - **Locally:** an nginx gateway serving the SPA, the Api host (BFF and REST), the Worker (outbox relay and SLA sweep), PostgreSQL, Redpanda, Zitadel, Mailpit, the Aspire Dashboard and the core-banking stub service (a stand-in for the bank's core banking, behind `ICoreBankingClient` and `ISimSwapCheck`).
  - **In AWS:** the same Compose stack, except that RDS replaces the PostgreSQL container and CloudWatch receives the telemetry through an OpenTelemetry Collector (CloudWatch's OTLP endpoints need SigV4 signing, which the .NET exporter can't do).
- **NFR-ARC-3:** v1 runs one Api instance and one Worker instance. Sessions, idempotency keys and the audit log are in PostgreSQL, so the Api scales out without session loss. In-memory rate-limit state is the one exception (FR-8). Scale-out itself is a production-path note.
- **NFR-ARC-4:** a second Worker instance would be harmless. The relay holds a PostgreSQL advisory lock, so only one relay publishes and per-dispute order is kept. The SLA sweep uses `SKIP LOCKED` (FR-26).
- **NFR-ARC-5:** external systems are reached only through the ports `ICoreBankingClient`, `ISimSwapCheck` and `ISmsSender`. The only single-implementation interfaces allowed are these three and `IAppDbContext`, the persistence seam the architecture tests enforce.

### 5.2 Resilience and operational readiness

- **NFR-RES-1:** the Api host exposes `/health/live` (the process is up) and `/health/ready` (dependencies are reachable, PostgreSQL included). The Worker exposes no HTTP: its outbox relay and SLA sweep each touch a heartbeat file every loop, and the Compose health check fails when either file is stale. Compose health checks use both.
- **NFR-RES-2:** calls to `ICoreBankingClient` go through the standard resilience handler: a timeout, retries with jitter on idempotent reads, and a circuit breaker. Once the policies are exhausted, the caller gets 503 ProblemDetails.
- **NFR-RES-3:** raising a dispute and taking actions keep working while Kafka is down. Events wait in the outbox and are published, in order per dispute, when Kafka returns. The relay retries with backoff and stops at the first failure in each dispute's sequence.
- **NFR-RES-4:** an outbox row the broker rejects permanently (not a transient failure) is marked failed after 5 attempts. It is counted in a metric and logged with its event ID. Later events for that dispute wait until an operator acts, as described in the README runbook [ASSUMPTION: a failed state in the outbox, not a dead-letter topic; see §9 Q4].
- **NFR-RES-5:** the Api host and the Worker shut down gracefully on SIGTERM, finishing in-flight requests and the current outbox batch within 30 seconds [ASSUMPTION].

### 5.3 Data and domain modelling

- **NFR-DATA-1:** PostgreSQL is the system of record for:
  - disputes and their transaction snapshots;
  - timelines;
  - links and OTP challenges;
  - sessions;
  - idempotency keys;
  - the audit log;
  - the outbox.

  Transactions stay in the core-banking source and are not copied, apart from the snapshot.
- **NFR-DATA-2:** a dispute change commits atomically with its timeline entry, its audit entry and its outbox event, or none of them commit.
- **NFR-DATA-3:** the allowed actions (FR-24), disputability (FR-12) and the amount rules (FR-14) live in the domain model, not in the handlers.
- **NFR-DATA-4:** money is `numeric(19,2)` with an ISO 4217 code, and every timestamp is stored in UTC. South African time (UTC+2, no daylight saving) is applied only for display and for the 90-day calendar.
- **NFR-DATA-5:** each dispute carries a version for optimistic concurrency. One dispute per transaction is enforced by a unique index on (account reference, transaction ID). Links have unique indexes on customer ID and on login.
- **NFR-DATA-6:** schema changes are EF Core migrations. They are applied at start-up through EF Core's `MigrateAsync`, under its migration lock, on `docker compose up`, and reviewed as high-stakes changes. A production deployment applies them with a migration bundle instead (EF Core documentation), recorded in `docs/production-path.md`.
- **NFR-DATA-7:** dispute records and their audit entries are never deleted in v1. Retention follows transaction records: five years under the FIC Act, then de-identification under POPIA s14 (dispute-lifecycle research, recommendation 5). The jobs that enforce this are a production-path note.

### 5.4 API design

- **NFR-API-1:** every route is under `/api/v1`. The built-in OpenAPI support generates the document, which is committed as `openapi.json` and checked for drift in CI. It is the frontend contract.
- **NFR-API-2:** every error is an RFC 9457 ProblemDetails response with a stable `type`, and every one includes the `traceId`, except the gateway's own 429, which has no trace context (AD-3). Status codes are used as the FRs specify.
- **NFR-API-3:** lists use keyset pagination with opaque cursors.
- **NFR-API-4:** each action has its own endpoint, named after the action. There is no generic "update status" endpoint.
- **NFR-API-5:** customer endpoint responses never contain an actor's identity or an SLA, and Escalated is mapped as FR-17 describes.
- **NFR-API-6:** money travels as decimal strings, and enum values use camelCase (FR-28).

### 5.5 Security (primary)

- **NFR-SEC-1:** the browser holds only the session cookie (FR-2). Every unsafe method on a customer or linking endpoint requires an antiforgery header, following OAuth 2.0 for Browser-Based Applications (the BFF pattern) and the OWASP CSRF Prevention Cheat Sheet.
- **NFR-SEC-2:** customer and linking endpoints accept only the cookie. Operations endpoints accept only bearer JWTs (client credentials), validated for issuer, audience, lifetime and signature. The wrong kind of credential gets 401.
- **NFR-SEC-3:** authorisation is enforced in the API on every endpoint and denies by default (OWASP Authorization Cheat Sheet), through the authorisation states (FR-9) and the roles (FR-21 to FR-23, FR-30). The SPA hiding a control is cosmetic only.
- **NFR-SEC-4:** linking prevents enumeration (FR-4, FR-6). OTPs are HMAC-hashed with a key that is not in the repo. Tests cover the rate limits, the send cap and the lockout (FR-4, FR-7, FR-8).
- **NFR-SEC-5:** ID numbers are never stored or logged. Card numbers are stored and shown masked only, as the first 6 and last 4 digits (dispute-lifecycle research, recommendation 5) [ASSUMPTION]. Account numbers are shown and stored as their last 4 digits only. The Api checks both masks where data enters from `ICoreBankingClient` and treats an unmasked number as core banking unavailable, so no unmasked number passes the core-banking boundary. Logs, traces, metrics and events carry no personal data (POPIA s10 and s19).
- **NFR-SEC-6:** ownership of disputes is enforced by an EF Core global query filter on the customer resolved from the link table. Ownership of transactions is checked through `ICoreBankingClient` (FR-11, FR-13). Another customer's resource returns 404, never 403 (RFC 9110 §15.5.4). Integration tests prove this for every customer endpoint. Only the operations endpoints and the Worker bypass the filter, each in one named place.
- **NFR-SEC-7:** the nginx gateway sets a strict CSP with no inline script, `X-Content-Type-Options`, `Referrer-Policy` and `frame-ancestors 'none'`, plus HSTS in AWS (OWASP HTTP Headers Cheat Sheet). Every `/api` response carries `Cache-Control: no-store` (RFC 9111), so no browser or shared cache keeps customer data. It applies a coarse limit of 10 requests per second per IP address, with a burst of 20 [ASSUMPTION]. Application limits use the built-in rate limiter.
- **NFR-SEC-8:** CI runs these scans, and any high or critical finding fails the build unless it is triaged in the repo [ASSUMPTION: threshold]:
  - CodeQL or Semgrep;
  - a vulnerable-package check;
  - gitleaks;
  - Trivy image scanning;
  - a ZAP baseline against the running stack.
- **NFR-SEC-9:** secrets come from the environment, never the repo. In AWS they come from SSM Parameter Store [ASSUMPTION], and Compose uses generated local values.
- **NFR-SEC-10:** the internal audit log is append-only from the application's side. It records every linking event (FR-10) and every dispute change: raise, withdraw, operations actions and SLA escalation (FR-13, FR-18, FR-25, FR-26).
- **NFR-SEC-11:** in AWS, Mailpit's web UI sits behind basic auth, with the credentials in SSM, because it shows OTPs. The README notes that a real deployment sends OTPs by SMS.

### 5.6 Code quality and maintainability

- **NFR-CODE-1:** .NET has nullable reference types on, analyzers treating warnings as errors, and `dotnet format` checked in CI. TypeScript is strict, with ESLint and Prettier checked in CI. No new suppression is added without a written reason.
- **NFR-CODE-2:** there is no MediatR, AutoMapper, Swashbuckle or Duende. The built-in feature is used wherever one exists: validation, ProblemDetails, `TimeProvider`, OpenAPI, the OIDC and cookie handlers, and the rate limiter.
- **NFR-CODE-3:** glossary terms are used in code, in the API and in UI copy.
- **NFR-CODE-4:** the frontend uses React and TypeScript, shadcn/ui on Tailwind, and TanStack Query hooks generated by Orval from `openapi.json`. Generated code is never edited by hand, and the design tokens live in one theme file.
- **NFR-CODE-5:** every new package is verified (publisher, downloads, repository, latest stable version) and pinned exactly.
- **NFR-CODE-6:** the customer portal meets WCAG 2.2 AA: text contrast, keyboard access, visible focus, labelled form errors and reflow at 320px, checked by axe in the Playwright E2E tests, plus a recorded manual keyboard pass (settled on 2026-10-27).

### 5.7 Testing (important)

- **NFR-TEST-1:** every FR consequence not tagged `[manual check]` maps to at least one automated test, and each story's report lists criterion → test.
- **NFR-TEST-2:** domain decisions have unit tests: the allowed actions (every cell of the FR-24 table), disputability including the D + 90 boundary, amounts, and SLA timing through `TimeProvider`.
- **NFR-TEST-3:** integration tests run against real PostgreSQL in Testcontainers. They cover the ownership filter, the uniqueness constraints, concurrent actions, parallel OTP verification, atomic outbox writes and idempotency. No in-memory database fakes are used.
- **NFR-TEST-4:** contract tests check `openapi.json` for drift and check each event type against its JSON Schema.
- **NFR-TEST-5:** thin Playwright E2E tests run UJ-1, UJ-2, UJ-5a, UJ-5b and UJ-5d through the real stack. QA traces each criterion through the browser, the network, the database and the events.
- **NFR-TEST-6:** no test is weakened to make it pass. CI runs every suite on every pull request.

### 5.8 Observability

- **NFR-OBS-1:** the SPA, the Api host and the Worker emit OpenTelemetry traces, metrics and logs. One trace runs browser → API → outbox → Kafka → Worker. The `traceparent` and `tracestate` are stored on the outbox row and injected into the Kafka headers by hand, with no prerelease Kafka instrumentation package.
- **NFR-OBS-2:** logs are structured, carry the trace ID, and contain no personal data.
- **NFR-OBS-3:** these business metrics are emitted for §8:
  - disputes raised, by reason;
  - actions, by action and actor type;
  - SLA escalations;
  - linking started, completed, locked and capped;
  - 409 and 422 rejections on raising;
  - failed outbox rows.

  Raise-screen opens and time to submit come from the SPA's browser spans.
- **NFR-OBS-4:** the Aspire Dashboard runs locally and CloudWatch is used in AWS. Both work with no manual setup after start-up.

### 5.9 Infrastructure and DevOps

- **NFR-INFRA-1:** `docker compose up` starts the whole stack locally with zero clicks, including Zitadel configuration, seed data and migrations. Images are built in multiple stages and run as non-root.
- **NFR-INFRA-2:** GitHub Actions builds, tests and scans every pull request, and pushes images to GHCR on main.
- **NFR-INFRA-3:** Terraform modules build one dev environment in a new AWS free-plan account:
  - an EC2 c7i-flex.large running the Compose stack;
  - RDS PostgreSQL db.t4g.micro;
  - two CloudFront distributions, one for the app and one for auth;
  - Mailpit as the demo inbox, protected as in NFR-SEC-11.
- **NFR-INFRA-4:** deployment uses a GitHub OIDC role, with no long-lived AWS keys. Terraform state is held in S3 with native locking. A budget alarm measures spend with credits excluded, so free-plan credits cannot hide real cost.
- **NFR-INFRA-5:** `terraform destroy` removes the environment cleanly [manual check] [ASSUMPTION].

### 5.10 Documentation and communication

- **NFR-DOC-1:** the README gives build, run and test steps, the demo logins and seed identities (FR-29), and how to drive the operations endpoints from Scalar. It ends with a map from each of the ten quality areas to where it is addressed.
- **NFR-DOC-2:** `docs/production-path.md` records everything deferred in §6.2, with a one-line reason for each.
- **NFR-DOC-3:** key decisions are short decision records in `docs/decisions/`, exactly these (settled on 2026-10-26):
  - ad-001 Zitadel as the external identity provider, standing in for the bank's identity platform;
  - ad-002 `IAppDbContext`;
  - ad-003 Result types;
  - ad-004 model roles;
  - ad-005 QA data integrity;
  - ad-006 actor-agnostic dispute actions;
  - ad-007 the BFF session, with no tokens in the browser;
  - ad-008 linking, including the SIM-swap check before the OTP is sent.

  The outbox and having no schema registry are recorded in the architecture spine (AD-14, AD-17, AD-19).
- **NFR-DOC-4:** Scalar is the API viewer in dev and demo, and stands in for another team's operations tool.
- **NFR-DOC-5:** the README states plainly the simplifications the research identified:
  - the 90-day customer window counted from the transaction date, set against the scheme's 120 days from its processing date;
  - the SLA as an internal trigger, not a promise (the National Financial Ombud Scheme gives banks 21 working days on referral, and the Conduct Standard asks for a reasonable time; dispute-lifecycle research, recommendation 2);
  - the fact that the primary scheme rule texts were not read (dispute-lifecycle research, caveat).

## 6. Scope

### 6.1 In scope

- Sign up and email verification, in the identity provider.
- Sign in and sign out through the BFF.
- Linking with an OTP.
- Viewing the customer's own transactions.
- Raising a dispute.
- Dispute history with a timeline.
- Customer withdrawal.
- Start review, escalate, resolve and read for authorised actors.
- SLA escalation.
- Dispute events.
- Seed data.
- Local Compose.
- One AWS dev environment.

### 6.2 Out of scope (recorded in `docs/production-path.md`)

- **Provisional credit:** a US Regulation E norm with no documented South African equivalent (dispute-lifecycle research).
- **Attachments.**
- **Customer notifications on status changes:** Mailpit carries only email verification, password reset and the linking OTP.
- **Customer self-unlink, and lost-login recovery:** both are bank-assisted in production.
- **Partial disputes other than wrong amount.**
- **Scheme-specific windows per reason:** for example the 540-day not-received extension, counted from the expected delivery date.
- **A separate fraud route for unrecognised transactions:** common in South African practice (dispute-lifecycle research, section 2).
- **A schema registry:** Redpanda's built-in registry is the upgrade path.
- **An event consumer in this repo:** a consumer would dedupe with an inbox table.
- **Operations features:**
  - a dead-letter topic;
  - outbox cleanup;
  - topic retention tuned to POPIA;
  - listing and search for operations tools.
- **A general support platform for several departments, and a UI for AI agents.**
- **Follow-up messages from the customer after raising a dispute.**
- **Retention and de-identification jobs** (NFR-DATA-7).
- **Scale-out:** several Api and Worker instances, shared rate-limit state, and latency targets under load.
- **Filters and search on transactions.**
- **Languages other than English, and currencies other than ZAR.**
- **The bank-scale end state:** linking moves into the bank's identity platform, downstream services consume the events for chargebacks, fraud and complaints reporting, and customers are notified when a status changes.

## 7. Non-goals

- DisputeDesk is not a case-management system. Operations work happens in another team's tool, which calls the API.
- It is not an identity platform. Zitadel stands in for the bank's.
- It does not raise chargebacks with card schemes.
- It never promises customers a resolution time.

## 8. Success metrics

There is no production baseline, so the targets are illustrative for the demo [ASSUMPTION]. They are measured from the NFR-OBS-3 metrics.

**Primary**
- **SM-1: Dispute submission completion.** Disputes submitted ÷ raise-dispute screens opened. Target: at least 80%. Validates FR-13 and FR-14.
- **SM-2: Time to submit.** Median time from opening the raise-dispute screen to the 201 response. Target: under 2 minutes. Validates FR-13.

**Secondary**
- **SM-3: Linking completion.** Logins linked ÷ logins that started linking. Target: at least 85%. Validates FR-4 to FR-6.
- **SM-4: Resolved before SLA escalation.** Disputes resolved without an SLA escalation ÷ all disputes resolved. Target: at least 70%. Validates FR-23 and FR-26.

**Counter-metrics (do not optimise)**
- **SM-C1: Rejected share.** Disputes resolved with outcome Rejected ÷ all disputes resolved. A proxy for duplicate or frivolous disputes. Counterbalances SM-1 and SM-2: an easier form must not invite poor disputes.
- **SM-C2: Linking abandonment and lockouts.** Logins that started linking and did not link within 24 hours, plus OTP lockouts and send-cap hits. Counterbalances SM-3: linking security must not be loosened to raise completion.
- **SM-C3: Quick withdrawals.** Disputes withdrawn within 1 hour of submission. A sign of mistakes or confusing copy. Counterbalances SM-1.
- **SM-C4: Support contacts about a dispute.** Measured outside DisputeDesk and noted for production. Counterbalances SM-4: a hidden SLA must not leave customers chasing the bank.

## 9. Open questions

None open. Settled on 2026-10-25:
1. A linked login calling a linking endpoint gets a 403 "already linked", which the SPA uses to redirect (FR-9).
2. Start review and escalate take no note; only resolve (required) and withdraw (optional) carry customer-visible notes (FR-24).
3. Timeline actor labels are "You" and "The bank" (§2.4).
4. Poison outbox rows get a failed status after 5 attempts; there is no dead-letter topic (NFR-RES-4).
5. The SIM-swap check runs before the OTP is sent (FR-4).
6. The other assumptions in §10 are confirmed as written.

## 10. Assumptions index

**Information architecture**
- §2.3: a minimal start page for signed-out visitors.

**Sign-in and linking**
- §4.1 FR-2: the session ends after 15 minutes idle or 12 hours in total.
- §4.2 FR-4: at most 3 OTP sends per customer per hour.
- §4.2 FR-4: a malformed ID or account number returns 400 without echoing the value or revealing a match.
- §4.2 FR-9: a linked login calling a linking endpoint gets a 403 "already linked".
- §4.2 FR-10: no unlink endpoint in v1; unlinking and recovery are bank-assisted.

**Transactions and raising a dispute**
- §4.3 FR-11: an account belongs to exactly one customer in v1.
- §4.3 FR-11: `limit` is an integer from 1 to 50, default 20; a bad `limit` or cursor returns 400.
- §4.3 FR-12: the 90 days are South African calendar days, up to and including D + 90.
- §4.4 FR-13: a non-disputable transaction returns 422.
- §4.4 FR-14: the expected amount is at least R0.00, below the transaction amount, with at most 2 decimal places.
- §4.4 FR-15: the key is 1–255 printable characters, and a missing key returns 400.
- §4.4 FR-15: keys are scoped to the customer and kept for 24 hours.

**History, withdrawal and actions**
- §4.5 FR-17: the guidance after a Rejected outcome is static copy only.
- §4.6 FR-18: the withdraw note is at most 500 characters.
- §4.7 FR-24: start review and escalate take no note.
- §4.7 FR-24: repeated actions return 409, with no idempotency key.
- §4.7 FR-25: one Zitadel service account per actor type.
- §4.7 FR-30: no operations list or search; tools find disputes from events.
- §4.8 FR-26: the SLA is 240 hours from submission.
- §4.8 FR-26: the sweep runs at least once a minute.

**Events and seed data**
- §4.9 FR-28: events leave out the customer ID and the amount.
- §4.10 FR-29: one seeded dispute passes the SLA a few minutes after start-up.
- §4.10 FR-29: customer D is added for linking; each E2E run starts on a fresh stack, which resets D's link.
- §4.10 FR-29: seeded disputes publish no events.

**Non-functional requirements**
- §5.2 NFR-RES-4: poison outbox rows get a failed state after 5 attempts, with no dead-letter topic.
- §5.2 NFR-RES-5: a 30-second graceful shutdown window.
- §5.5 NFR-SEC-5: card masking keeps the first 6 and last 4 digits.
- §5.5 NFR-SEC-7: the gateway limits each IP address to 10 requests per second, with a burst of 20.
- §5.5 NFR-SEC-8: high or critical scan findings fail the build.
- §5.5 NFR-SEC-9: secrets come from SSM Parameter Store in AWS.
- §5.9 NFR-INFRA-5: `terraform destroy` is clean.

**Success metrics**
- §8: the targets are illustrative.
