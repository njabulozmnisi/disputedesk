# Postmortem: records holding personal data keep the generated ToString (recurring)

Trigger: a recurring escape category (AGENTS.md, Continuous improvement). Written 2026-10-30.

## What recurs

A C# record's generated `ToString` prints every property. A record that holds personal data (an ID number, account number, customer ID, login `sub`, note) leaks it if anything ever logs or interpolates the record (OWASP Logging Cheat Sheet; ASVS 5.0 V16.2.5).

| Story | Caught by | Should have been caught by | Records |
|---|---|---|---|
| 3.8 | security | builder | `StartLinkingRequest` (fixed; origin of the review-story pitfall) |
| 5.1 / 5.2 | security (5.2 F3) | builder (5.1) | `OperationsDisputeResponse`, `OperationsTimelineEntryResponse` |
| 5.4 | security (L1) | builder | `FixtureAccount`, `FixtureCustomer`, `FixtureLogin`, `FixtureDispute`, `FixtureTransaction` |

Category: missing test. The countermeasure today is one line under the review and security skills' Known pitfalls. Builders do not load it, and the builder in 5.4 read it and chose to deviate.

## Proposal (needs a human decision)

A repeated mistake becomes a check. Add one architecture test: every `record` in `DisputeDesk.Contracts`, `DisputeDesk.Application` and `DisputeDesk.Infrastructure` that has a property on a short list of personal-data names (`IdNumber`, `AccountNumber`, `CustomerId`, `LoginSub`, `UserId`, `Note`, `MaskedCard`, `AccountRef`, `TransactionId`) declares a `sealed override ToString` (checked by reflection: `MethodInfo.DeclaringType == type`, `IsFinal`). Then delete the pitfall line from the skills.

- It fails on main today (the customer `DisputeResponse` and others), so it lands with a small production change sealing those records, which makes it a Small-tier change with production code. It needs approval, or a story.
- The name list is a shortcut. A record that holds personal data under another name escapes it. Upgrade path: an attribute (`[PersonalData]`, as ASP.NET Core Identity uses) on each such property, checked the same way.

## Decision (2026-10-30)

Approved: add the architecture test with the sealed records, as a Small-tier change through a builder and the reviewer, then delete the pitfall line from the review and security skills. It starts after 5.2 and 5.4 merge, because their fix rounds seal `OperationsDisputeResponse`, `OperationsTimelineEntryResponse` and the fixture records; starting earlier would edit the same records twice.
