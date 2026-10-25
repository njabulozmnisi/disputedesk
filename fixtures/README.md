# fixtures

`local.json` is the single source of truth for the local Compose environment: the synthetic customers,
their bank-record details (standing in for the core banking system) and their dev logins. The Zitadel
provisioner reads `logins`; later stories read `customers` and fill `transactions`.

## Synthetic, but handle it as if it were real

- **Made up, yet it may collide with real people.** The ID numbers are format-valid South African ID numbers
  (date of birth, sequence, citizenship digit, Luhn check digit), and the cellphone numbers use the real
  `+2782` mobile range. A format-valid number can belong to a real person, so treat these values as personal data.
- **Never log an ID number,** in any environment, including these fixtures. Logs, traces and metric labels
  must not carry them (POPIA; see `docs/conventions/backend.md`).
- **The local SMS sender must be a stub** that can never reach a real SMS gateway, so no message ever goes to
  one of these numbers. Any `ISmsSender` wired into the local platform writes to a log or a local inbox only.
- Passwords here are dev-only values for the local stack and must never be used anywhere else.
