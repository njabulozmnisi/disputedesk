---
title: 'Product Brief: DisputeDesk'
status: final
created: 2026-10-25
updated: 2026-10-25
inputs:
  - docs/glossary.md
  - docs/research/dispute-lifecycle/dispute-lifecycle.md
  - docs/research/event-versioning/event-versioning.md
---

# Product Brief: DisputeDesk

## Executive summary

DisputeDesk is a transaction dispute portal for a South African retail bank. A customer signs in, links their login to their customer record at the bank, sees their recent transactions, disputes a transaction that looks wrong, and follows each dispute through a timeline until it is resolved or withdrawn. Behind the portal, the bank's operations tooling and automated processes move disputes forward through the same API, and every change is published as an event for the rest of the bank.

It meets this business requirement: "The bank requires a customer-facing web application, with the back-end services that support it, through which customers can view their account transactions and raise a dispute against any transaction they believe is incorrect. Customers must also be able to view the history of their disputed transactions, including the current status of each dispute." It is built to production standards and runs locally with one `docker compose up`.

It is deliberately small: each of the ten quality areas is met once, done well, and anything bigger goes to `docs/production-path.md`.

## The problem

When a customer sees a card transaction they don't recognise, or one that is wrong, they have to phone the bank or fill in a form. After that they rarely know where their dispute stands. The bank, for its part, has to raise a chargeback within the card scheme's 120 days, so disputes that arrive late or incomplete use up the time it has to act (dispute-lifecycle research, section 1). Self-service fixes both problems if it is safe to use: it must prove that the person behind a login really is the customer, show that customer only their own data, and leave an audit record of every action.

## The solution

Six screens: sign in or sign up (hosted by the identity provider), link the login to the customer record, transactions, raise a dispute, dispute history, and dispute detail with its timeline.

- **Link.** A new login proves it belongs to a customer with bank details and a one-time code (OTP) sent to the cellphone number the bank holds. Until then it reaches only the linking screens.
- **View.** A linked customer sees about 90 days of transactions on one or two accounts, newest first. Disputed rows show their status.
- **Dispute.** For a disputable transaction (a settled card debit from the last 90 days that has never been disputed) the customer picks one of six reasons, adds an optional note, and disputes the full amount (or the difference, for wrong amount).
- **Follow.** The dispute history lists every dispute the customer has raised. Each one has a timeline that shows each status change, when it happened (South African time), which type of actor made the change, and any note meant for the customer.
- **Move forward.** Operations tools and automations start review, escalate and resolve disputes through a role-protected API. The customer can withdraw a dispute that is still open. A dispute that is unresolved 10 calendar days after submission is escalated automatically.

## What makes this different

There is no moat here, and the brief does not pretend otherwise. The differences are in how carefully it is built:

- **Real identity separation.** Authentication belongs to a self-hosted identity provider, which stands in for the bank's identity platform. Authorisation is DisputeDesk's own job. Holding a login gives no access to customer data until the login is linked to a customer, and that customer is always resolved on the server, never taken from the request.
- **Actions that ignore who the actor is.** A person using an operations tool, an LLM agent and a scheduled process all call the same actions under the same rules. The type of actor is recorded for audit and is never used to decide anything.
- **A history that can be trusted.** Every status change is written to the timeline and to an event in the same transaction, so the customer's view and the bank's event stream always agree.
- **Calibrated to real norms.** The windows, reasons and SLA follow the dispute-lifecycle research.

## Who this serves

- **Bank customers** (the people using the portal). They want to dispute a transaction in under two minutes without phoning anyone, and to see at any time where their dispute stands.
- **Operations and automation** (actors calling the API). Support tooling, LLM agents and scheduled processes that review, escalate and resolve disputes. For the demo, the Scalar API viewer stands in for another team's operations tool.
- **Engineers who maintain or review the solution.** The requirements put security first and testing a close second, and treat over-engineering as a defect as serious as missing rigour. They need to see within minutes how each area is handled: every acceptance criterion proved by a test, one trace from browser to Worker, clean security scans, and a README that ends with a map from each area to where it is addressed.

## Success criteria

The portal is a demo, so no production baseline exists and the targets below are illustrative [ASSUMPTION]. Each metric can be measured from DisputeDesk's own telemetry.

| Metric | Signal | Illustrative target |
|---|---|---|
| Dispute submission completion | Disputes submitted ÷ raise-dispute screens opened | ≥ 80% |
| Time to submit | Median time from opening the raise-dispute screen to submission | < 2 minutes |
| Linking completion | Logins linked ÷ logins that started linking | ≥ 85% |
| Resolved before SLA | Disputes resolved before the SLA escalation ÷ disputes resolved | ≥ 70% |

**Counter-metrics:** share of disputes resolved with outcome Rejected (duplicate or frivolous disputes); linking abandonment and OTP lockouts; withdrawals within an hour of submission; support contacts about a dispute (measured outside DisputeDesk).


## Scope

**In:** sign up and email verification (in the identity provider), linking a login to a customer, viewing the customer's own transactions, raising a dispute, dispute history with a timeline, withdrawal by the customer, the start review, escalate and resolve actions for authorised actors, automatic escalation when the SLA runs out, dispute events on Kafka, seeded demo customers, and a single AWS dev environment built with Terraform.

**Out (recorded in `docs/production-path.md`):** provisional credit, attachments, notifications to customers when a status changes, unlinking by the customer, partial disputes other than wrong amount, separate scheme windows per reason, a schema registry, an event consumer inside this repo, a general support platform for several departments, a UI for AI agents, and follow-up messages from the customer after a dispute is raised.

## Vision

If this were run for real, DisputeDesk would become the bank's dispute front door. Linking would move into the bank's identity platform. Downstream services would consume the events for chargebacks, fraud and complaints reporting, and customers would be notified when a status changes. The core would stay the same: one dispute per transaction, actions that work the same for every actor, and a timeline the customer can trust.
