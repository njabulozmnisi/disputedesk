# Glossary

Domain terms, settled during grilling. Use these words; avoid the synonyms listed.

| Term | Meaning | _Avoid_ |
|---|---|---|
| **Customer** | A person the bank holds an account for, identified by the bank's customer ID. | client, user (for the bank relationship) |
| **Login** | A person's sign-in identity with the identity provider. A login is not a customer until it is linked. | account (ambiguous with bank account), user |
| **Linked customer** | A login that has proved it belongs to a customer and is linked to that customer's ID. | verified user, enrolled user |
| **Account** | A bank account that holds transactions. | wallet, profile |
| **Transaction** | A settled or pending movement on an account, read from the bank's core banking system. | payment, charge, debit (as a general term) |
| **Disputable transaction** | A settled card debit posted within the last 120 days that has never been disputed. | eligible payment |
| **Dispute** | A customer's claim that a transaction was wrong, raised for its full amount. One per transaction, ever. | claim, chargeback, complaint, case, ticket |
| **Reason** | Why the customer disputes: unrecognised, duplicate charge, wrong amount, not received, cancelled subscription. | category, type |
| **Status** | Where a dispute is: Submitted, Under review, Escalated, Resolved, Withdrawn. | state (in customer-facing copy), stage |
| **Outcome** | The result of a resolved dispute: Upheld (the customer was right) or Rejected. | decision, verdict, result |
| **Action** | A named change to a dispute's status: start review, escalate, resolve, withdraw. Any authorised actor may perform it. | transition (in copy), operation, command |
| **Actor** | Whoever performed an action: the customer, an operations tool, or an automated process. Recorded, never used to decide. | agent (ambiguous with AI agents), user, staff |
| **SLA** | The 10 calendar days from submission within which a dispute should be resolved; after that it is escalated automatically. | deadline, TAT |
| **Timeline** | The ordered history of a dispute's status changes, with notes meant for the customer. | audit log (that is internal), activity feed |
