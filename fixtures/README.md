# fixtures

`local.json` is the single source of truth for the local Compose environment: the synthetic customers,
their bank-record details (standing in for the core banking system), their accounts and transactions, and their
dev logins. The Zitadel provisioner reads `logins`; the core-banking stub (`backend/stubs/DisputeDesk.CoreBankingStub`)
reads `customers` and `transactions`.

## What is in it

| Customer | Accounts | Login | Role in the demo |
| --- | --- | --- | --- |
| A, `CUS-000001` | `ACC-A-1`, `ACC-A-2` | `customer-a` | Linked. Later stories give A a dispute in every status. |
| B, `CUS-000002` | `ACC-B-1` | `customer-b` | Linked. Owns the dispute A gets 404 for. |
| C, `CUS-000003` | `ACC-C-1` | `customer-c` | Not linked. `recentSimSwap: true`, so the SIM-swap check fails and linking is refused. |
| D, `CUS-000004` | `ACC-D-1`, `ACC-D-2` | none | Not linked and passes every check. A new login links to D in the linking demo. |

- **Accounts** carry an explicit, opaque `accountRef`. The full `accountNumber` is the bank's record that linking
  matches against; the stub never returns it, only `accountRef` and the last 4 digits.
- **ID shape.** Every `accountRef` and `transactionId` is 1 to 32 upper-case letters, digits and hyphens
  (`[A-Z0-9-]{1,32}`, like `ACC-A-1` and `TXN-A-0001`); the core-banking client treats any other value as not found.
- **Transactions** carry an explicit `transactionId` and the `accountRef` they belong to. Each customer has about
  90 days of them, including one pending card debit, one credit, one own-account transfer and one older than
  90 days. Cards are stored already masked (`maskedCard`, first 6 and last 4 digits, or null); no full card
  number exists anywhere in this file.
- **Dates are relative,** so the data never goes stale. A transaction's date is `daysAgo` days before the start of
  the current South African day (UTC+02:00), at `time` South African time. The stub resolves them once, when it
  starts; the seeder uses the same anchor (architecture AD-30). After a day boundary, restart the stack to move the
  dates on.

## Synthetic, but handle it as if it were real

- **Made up, yet it may collide with real people.** The ID numbers are format-valid South African ID numbers
  (date of birth, sequence, citizenship digit, Luhn check digit), and the cellphone numbers use the real
  `+2782` mobile range. A format-valid number can belong to a real person, so treat these values as personal data.
- **Never log an ID number,** in any environment, including these fixtures. Logs, traces and metric labels
  must not carry them (POPIA; see `docs/conventions/backend.md`).
- **The local SMS sender must be a stub** that can never reach a real SMS gateway, so no message ever goes to
  one of these numbers. `ISmsSender` delivers over SMTP to the configured host only, which is Mailpit locally.
- Passwords here are dev-only values for the local stack and must never be used anywhere else.
