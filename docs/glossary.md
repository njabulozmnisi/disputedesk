# Glossary

Domain terms, settled during grilling. Use these words; avoid the synonyms listed.

| Term | Meaning | _Avoid_ |
|---|---|---|
| **Customer** | A person the bank holds an account for, identified by the bank's customer ID. | client, user (for the bank relationship) |
| **Login** | A person's sign-in identity with the identity provider. A login is not a customer until it is linked. | account (ambiguous with bank account), user |
| **Linked customer** | A login that has proved it belongs to a customer and is linked to that customer's ID. | verified user, enrolled user |
| **Linking** | How a login proves it belongs to a customer: their ID number and account number, then a one-time code sent to the cellphone on the bank's record. | enrolment, onboarding, registration |
| **OTP** | The one-time code used in linking: 6 digits, valid for 5 minutes, usable once. | PIN, verification code, token |
| **Account** | A bank account that holds transactions. | wallet, profile |
| **Transaction** | A settled or pending movement on an account, read from the bank's core banking system. | payment, charge, debit (as a general term) |
| **Transaction date** | The date the card transaction was made, in South African time; the 90-day window counts from it. | posting date, settlement date |
| **Disputable transaction** | A settled card debit made within the last 90 days that has never been disputed (the bank then has time to raise a chargeback within the card scheme's 120 days). | eligible payment |
| **Dispute** | A customer's claim that a transaction was wrong, raised for its full amount, or for the difference when the reason is wrong amount. One per transaction, ever. | claim, chargeback, complaint, case, ticket |
| **Reason** | Why the customer disputes: unrecognised, duplicate charge, wrong amount, not received, cancelled subscription, refund not processed. | category, type |
| **Status** | Where a dispute is: Submitted, Under review, Escalated, Resolved, Withdrawn. Customers see Escalated as Under review (prioritised). | state (in customer-facing copy), stage |
| **Outcome** | The result of a resolved dispute: Upheld (the customer was right) or Rejected. | decision, verdict, result |
| **Action** | A named change to a dispute's status: start review, escalate, resolve, withdraw. Any authorised actor may perform it. | transition (in copy), operation, command |
| **Actor** | Whoever performed an action: the customer, an operations tool, or an automated process. Recorded, never used to decide. | agent (ambiguous with AI agents), user, staff |
| **SLA** | The 10 calendar days from submission after which an unresolved dispute is escalated automatically. Internal only: never promised to customers. | deadline, TAT |
| **Dispute event** | A fact the bank publishes when a dispute is raised or its status changes, for other systems to react to. Carries no personal data. | message, notification, webhook |
| **Timeline** | The ordered history of a dispute's status changes, with notes meant for the customer. | audit log (that is internal), activity feed |
