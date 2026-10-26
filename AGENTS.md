# AGENTS.md

Rules for any AI assistant or person working in this repo. Area detail lives in
[docs/conventions/backend.md](docs/conventions/backend.md) and
[docs/conventions/frontend.md](docs/conventions/frontend.md).

## Priority order

Correctness, then security, then data integrity, then maintainability, then performance, then brevity.

## How to work

- Read before writing. Make the smallest change that satisfies the acceptance criteria. Leave the tree green.
- Framework first: check the .NET 10 docs (Microsoft Learn MCP, or `npx @microsoft/learn-cli`) before writing any abstraction. Use the built-in way where one exists: minimal API validation, ProblemDetails, `TimeProvider`, built-in OpenAPI, the built-in OIDC and cookie handlers, the built-in rate limiter.
- Every abstraction earns its place. No MediatR, AutoMapper, Swashbuckle or Duende; no single-implementation interfaces except `ICoreBankingClient`, `ISimSwapCheck`, `ISmsSender` and `IAppDbContext`.
- **Stupid simple and logical code.** Write the plainest code that meets the criteria, so a developer new to the repo understands it on first read: obvious control flow, early returns, short methods, descriptive names, no cleverness, no speculative options or extension points, no wrapper that only forwards calls. Prefer a few lines of plain duplication over a premature abstraction; abstract on the third real case. Before reporting, reread the diff and cut anything not needed.
- Over-engineering is the standing risk. Show each assessment area once and well; anything bigger goes in `docs/production-path.md`. Mark a deliberate shortcut in code with `// shortcut: <the limit>, <when to upgrade>`.
- Use the terms in [docs/glossary.md](docs/glossary.md). Don't invent synonyms.

## Stakes tiers

| Tier | Examples | Ceremony |
|---|---|---|
| Trivial | typo, comment | Change, run the affected check |
| Small | one endpoint field, one component state | Test first, full area checks |
| Feature | a story | The full story flow: builder, verifier, QA, reviewer |
| High-stakes | auth, identity and account linking, customer data, money, migrations | Feature flow plus the security agent and a human review of the diff |

## Dependency verification

Before adding any package, confirm it exists, is the real one (publisher, download count, repository) and is the latest stable version. Pin it exactly. Hallucinated package names are a supply-chain risk.

## When stuck

Never weaken a test to make it pass. After two failed attempts, or before anything destructive or irreversible, stop and return `blocked` with the reason. Don't ask for permission mid-task; report.

## Status contract

Every pipeline agent ends with one JSON object:

```json
{"status": "built", "assumptions": [], "open_questions": [], "conflicts": []}
```

Builders add `"criteria": [{"id": "...", "test": "..."}]` mapping each acceptance criterion to the test that proves it.

## Finish checklist

- Tests prove every acceptance criterion; the report maps criterion to test.
- The developer smoke check passed.
- No new suppressions, no new packages without verification.
- No forbidden terms.
- The report lists assumptions and open questions.
