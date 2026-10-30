# ad-008: Linking, with the SIM-swap check before the OTP is sent

**Status:** accepted (records AD-12; guess-bound wording decided 2026-10-30, 7.6 challenge, replacing the 2026-10-27 wording)

**Context.** Linking is how a login proves it belongs to a customer: the ID number and account number, then an OTP sent to the cellphone on the bank's record (glossary). Two risks shape it. Linking must not reveal whether a person banks here, through its response, its timing or a count of tries left (FR-4, AD-12). And the OTP is the "something it holds" factor (PRD §4.2), so the SIM-swap check on the cellphone on record runs before any OTP is sent, and fails closed (FR-4; AD-12, *Order*).

**Decision.** One uniform request, with the checks and the send off the request path (AD-12, FR-4 to FR-8).

- **Start.** A well-formed start returns 202 with one fixed body (429 to everyone when the bounded queue is full), writes a challenge and queues a job on a bounded in-memory channel. The ID number is held in memory only, never persisted or logged.
- **The job.** Core-banking match, link check, then one transaction under a per-customer advisory lock: the send cap, then `ISimSwapCheck`, which fails closed, then the claim; only then `ISmsSender`. No OTP is ever sent before the SIM-swap check passes.
- **The OTP.** 6 digits, stored only as an HMAC, valid for 5 minutes, usable once (FR-5).
- **Verify.** One atomic conditional update counts the attempt before the code is compared; the challenge locks after three wrong codes, and a correct code on the third attempt still links (FR-7).
- **Rate limits.** 5 attempts per login and 20 per client IP in any 15 minutes, start and verify combined, through the built-in sliding-window limiter (FR-8).

**What bounds OTP guesses, stated as it is.**

- Per challenge: the lockout after three wrong codes (FR-7).
- Per login window: the per-login and per-IP linking rate limits (FR-8). They are held in memory in the one Api instance and reset when it restarts.
- The send cap of three OTPs per customer per hour (FR-4) limits codes issued, not verification attempts.
- This is not the account-wide limit on failed attempts that NIST SP 800-63B describes ([SP 800-63B-4, *Rate Limiting (Throttling)*](https://pages.nist.gov/800-63-4/sp800-63b.html)). Restarting linking creates a fresh challenge with 0 attempts, and no control counts failed attempts across challenges; the only limit across challenges is the in-memory 15-minute windows, which count every start and verify. These controls are not an overall bound on guesses.
- The upgrade is a persistent per-customer failure counter that survives restarts and new challenges: entry P30, *OTP guess bound*, in `docs/production-path.md`. No control changes in v1 (human decision, 2026-10-30).

**Options rejected** (AD-12).

- *The core-banking lookup inside the request:* its timing would reveal a match.
- *Handing jobs to the Worker through a table:* it would persist data derived from the ID number.
- *Sending before the SIM-swap check.*
- *A persistent per-customer failure counter in v1:* deferred with no control change (human decision, 2026-10-30).

**Consequences.**

- Status, body and timing are the same whether the details match, match nothing, or match an already linked customer (FR-4).
- An Api restart drops queued jobs, so the person sees wrong codes and starts again (AD-12, *Restart*, an assumption).
- A failed SMS send still counts toward the hourly cap, so a customer hit by delivery failures waits for the window (AD-12; production-path entry P31, *Linking send cap and delivery*).
- Shared limiter state for several Api instances is part of the production-path entry P33, *Scale-out and shared limit state*.
