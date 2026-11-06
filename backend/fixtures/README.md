# fixtures

`local.json` is the one source of seed data for the local Compose stack and the AWS demo (where `Seed:Enabled` is on): the synthetic customers, their bank records (standing
in for core banking), accounts, transactions, logins and seeded disputes.

| Reader | Reads |
|---|---|
| Zitadel provisioning | `logins` |
| The core-banking stub | `customers`, `transactions` |
| The Api's seeder | `customers`, `logins`, `transactions`, `disputes` |
| `scripts/dev-login.sh` | `logins` |
| The e2e specs and `e2e/run-regression.sh` | `logins`, `customers`, `disputes` |

## Customers

| Customer (key) | Sign-in username (login key) | Accounts | Role in the demo |
| --- | --- | --- | --- |
| A (`A`), `CUS-000001` | `thandi.nkosi` (`customer-a`) | `ACC-A-1`, `ACC-A-2` | Linked. A seeded dispute in every status and outcome, plus the SLA demo dispute |
| B (`B`), `CUS-000002` | `pieter.vanwyk` (`customer-b`) | `ACC-B-1` | Linked. One seeded dispute, the one A gets 404 for |
| C (`C`), `CUS-000003` | `ayesha.patel` (`customer-c`) | `ACC-C-1` | Not linked. `recentSimSwap: true`, so linking is refused |
| D (`D`), `CUS-000004` | none | `ACC-D-1`, `ACC-D-2` | Not linked and passes every check. A new login links to D |

- **Accounts** have an opaque `accountRef`. The full `accountNumber` is the bank's record that linking matches. The
  stub never returns it, only `accountRef` and the last 4 digits.
- **IDs.** Every `accountRef` and `transactionId` matches `[A-Z0-9-]{1,32}`, like `ACC-A-1` and `TXN-A-0001`. The
  core-banking client treats any other value as not found.
- **Transactions.** Each customer has about 90 days of them. Among them are at least one pending card debit, one
  credit, one own-account transfer, and one older than 90 days. Cards are stored masked (first 6 and last 4 digits),
  and no full card number exists in this file.
- **Dates are relative,** so the data never goes stale. A date is `daysAgo` days before the start of the current South
  African day (UTC+02:00), at `time` South African time. The stub and the seeder share this anchor (AD-30). After a day
  boundary, restart the stack to move the dates on.

## Seeded disputes

On a fresh stack the seeder writes:
- for A, all on `ACC-A-1`: one Submitted, two Under review (one is the SLA demo dispute), one Escalated, one Withdrawn,
  one Resolved Upheld and one Resolved Rejected;
- for B: one Submitted.

Each is raised through the `Dispute` aggregate and taken through its actions, so every action has its stored event and
outbox row. A restart adds nothing.

- **An entry** names:
  - the `customer`, by key (a linked customer: `A` or `B`);
  - one of that customer's transactions (`accountRef`, `transactionId`);
  - the `reason`;
  - an optional `note` of up to 1000 characters;
  - for `wrongAmount` only, an `expectedAmount` as a decimal string.
- **`actions`** start with `raise`. Then come any of `startReview`, `escalate`, `resolve` (with an `outcome` and a
  1–500 character `note`) and `withdraw` (an optional `note` of up to 500), in an order the dispute allows.
  - Each action has a `daysAgo` and `time`. The SLA demo dispute is the exception: its raise has no instant at all, and its later actions use `hoursAfterRaise`.
  - The instants must be strictly increasing, after the transaction, and not after seeding.
- **The SLA demo dispute** (`slaDemo: true`, on exactly one entry) is raised 240 hours before seeding plus 5 minutes, so it falls due
  5 minutes after start-up.
- **The other Submitted and Under review disputes** are, by convention, raised at most 2 days before the anchor, so
  the first sweep leaves them alone. `SeededDisputesTests` checks they are under 10 days old at seeding.
- **A bad entry stops the seed,** usually with an error naming its position (`disputes[3]`), and the Api never becomes
  healthy. The checks run before any dispute is written, except when the `Dispute` aggregate itself refuses an action.
  A field in the wrong place is a bad entry too: an `outcome` or `note` on an action that takes none, or
  `hoursAfterRaise` anywhere but the SLA demo dispute's later actions. Unknown fields are ignored.
- **Notes are synthetic.** A note holds no ID, phone or account number, no name, and no run of 6 or more digits. A
  fixture test checks this.
- **B's dispute ID** is a new UUIDv7 on each fresh stack. Sign in as B and read it from `GET /api/v1/disputes`.

## Synthetic, but handle it as if it were real

- **The ID numbers are format-valid** South African IDs, and the cellphone numbers use the real `+2782` range. Treat
  them as personal data.
- **Never log an ID number,** in any environment, these fixtures included (POPIA; `docs/conventions/backend.md`).
- **The SMS sender must stay a stub.** `ISmsSender` delivers over SMTP to the configured host, which is Mailpit
  locally, so no message reaches a real number.
- **The passwords are dev-only** and must never be used anywhere else.
