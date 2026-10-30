# ad-002: EF Core directly behind `IAppDbContext`

**Status:** accepted (records AD-2)

**Context.** Application handlers need to read and write data, but Application must not bind to a database provider: Domain and Application reference no infrastructure, and an architecture test enforces it (NFR-ARC-1). AGENTS.md allows a single-implementation interface only for `ICoreBankingClient`, `ISimSwapCheck`, `ISmsSender` and `IAppDbContext` (NFR-ARC-5). AD-2 also guards against a repository layer growing beside EF Core and against every story editing the same interface.

**Decision.** Handlers use EF Core directly through one narrow interface, `IAppDbContext` (AD-2).

- It exposes `Entities<TEntity>()`, which `AppDbContext` forwards to `Set<TEntity>()`, and `SaveChangesAsync`. The member is not named `Set` because analyzer CA1716 rejects a keyword as a member name (decided 2026-10-28, story 3.1).
- A story adds an entity and its `IEntityTypeConfiguration` in its own feature folder and never edits the interface.
- Application may reference `Microsoft.EntityFrameworkCore`, never `Npgsql.*`; only Infrastructure implements `IAppDbContext`, which `LayerTests` checks.
- Raw SQL is allowed only where EF Core cannot express the statement (`FOR UPDATE SKIP LOCKED`, advisory locks, the conditional linking updates), in one named, parameterised Infrastructure method with a justification comment.
- Names are PascalCase, the EF Core and Npgsql default, so no naming-convention package is added (AD-2, AD-22).
- Each story adds one migration, generated after rebasing onto main; CI runs `dotnet ef migrations has-pending-model-changes`.

**Options rejected** (AD-2).

- *Repository and unit-of-work wrappers:* the repository layer growing beside EF Core that AD-2 exists to prevent.
- *Dapper for reads:* rejected in AD-2; reads stay on EF Core.
- *A `DbSet` property per entity on the interface:* every story would edit the same interface.

**Consequences.**

- Handlers write plain LINQ against `Entities<TEntity>()`, and one `SaveChangesAsync` is one transaction (EF Core *Transactions*), which the one commit path for dispute changes relies on (AD-14).
- The interface stays two members long however many entities are added.
- Each raw-SQL method is a named exception that a reviewer can find and check; the idempotency claim is one of them today (AD-16), and an EF Core-only replacement is a proposal in `docs/production-path.md`.
- A snapshot conflict between two migrations is resolved by deleting and regenerating, never by hand-merging, so no two stories touch migrations at once (AD-2, AD-22).
