---
id: 5
type: story
title: "Architecture source rules read Roslyn syntax trees, and banned APIs fail the build"
parent: epic-8-simplicity-sweep
covers: [R25, R35]
after: [4]
hitl: false
risk: low
refined: true
---

# Architecture source rules read Roslyn syntax trees, and banned APIs fail the build

## Description

Slice S4 of the mitigation plan (wave 2), with wave 4's first countermeasure. The architecture tests read production C# as text and match it with regular expressions; because a regex does not understand C#, each rule needs its own sample-source self-tests (about 100 `[InlineData]` cases), and one comment-blanking regex already caused a real escape (TST-2; `flaky-fixes.md`, 5.3 security N1). This story moves each C# rule onto a Roslyn syntax tree, where comments, strings, spacing and line breaks are the parser's job, deletes the regex self-tests, and uses `Microsoft.CodeAnalysis.BannedApiAnalyzers` for the rules that are simply "this API is banned in this project", including the new ban on hand-written `ILoggerProvider`s in test projects once 8.4 has removed them. The rules and their intent do not change; `LayerTests` already uses assembly metadata and stays.

## Requirements

Verify: the ArchitectureTests project and the full backend build pass with warnings as errors; every rule still fails on its Supervisor check below; the deleted tests are listed in the report.

Inputs: none (no endpoint or production change).

1. **C# source rules read syntax trees** (TST-2; R25)
   **Given** the rules in `backend/tests/DisputeDesk.ArchitectureTests/SourceRulesTests.cs`, `OwnershipRulesTests.cs`, `AuditLogRulesTests.cs`, `UsingAliasRulesTests.cs`, `ProblemCatalogueTests.cs` and `PlatformScriptTests.ApiProgram_ForwardedHeaders_RunFirstInThePipeline`
   **When** a rule reads a `.cs` file
   **Then** it inspects syntax nodes and trivia from `CSharpSyntaxTree.ParseText`, never a regex over the text, and reports each violation as `<relative path>:<line>` from the node's location; each scan parses each file once and shares the tree among the rules that read it (one cached parse per file in the test project, checked by reading the code in review)
   **And** the folder and file predicates each rule uses today (for example `BypassFolders`, `MayNameAuditEntries`) are kept unchanged

2. **The actor and namespace rules match syntax nodes** (TST-2; R25, AD-14a, AD-11)
   **Given** `ProductionSource_AutomationActorType_AppearsOnlyInTheSweepTheSeederAndTheWireMapping` and `CustomerFeatureSource_OperationsOrSlaNamespace_IsNeverReferenced`
   **When** they run
   **Then** the first finds every `MemberAccessExpressionSyntax` whose name is `Automation` on an expression ending in `ActorType`, and the second every `using` directive, qualified name (`QualifiedNameSyntax`) or member-access expression (`MemberAccessExpressionSyntax`, a fully qualified name used as an expression) naming an `Operations` or `Sla` namespace of the project, with the same allowed folders as today; text in comments and string literals is never matched
   **And** the namespace rule stays syntactic: it matches the project's namespace names as written, as today, and does not resolve what a short name denotes

3. **The query-filter rules match invocations** (TST-2; R17, AD-11)
   **Given** `ProductionSource_IgnoreQueryFilters_AppearsOnlyInOperationsAndSla` and `ProductionSource_ParameterlessIgnoreQueryFilters_IsAbsent`
   **When** they run
   **Then** each finds `InvocationExpressionSyntax` nodes calling `IgnoreQueryFilters`, the first by folder as today (`BypassFolders`, `OwnershipRulesTests.cs:18-21`), the second when the invocation has no argument

4. **The audit-log rules match invocations, object creations and table names** (TST-2; R10, R41, AD-14)
   **Given** `ProductionSource_FileNamingAuditEntriesOrStoredEvents_HasNoBulkOrRawSqlCall` and `ProductionSource_FileOtherThanAuditLog_NeverConstructsOrAddsAnAuditEntry`
   **When** they run
   **Then** a file that has an identifier starting with `AuditLogEntr` or `StoredEvent`, or a string literal or interpolated-string text containing `AuditLogEntries` or `StoredEvents` (today's prefix match, `AuditLogRulesTests.cs:98`), fails on any invocation whose name starts with `ExecuteUpdate`, `ExecuteDelete`, `ExecuteSql` or `SqlQuery`, or ends with `SqlRaw` (each with its `Async`, `Interpolated` and generic forms)
   **And** every file outside `MayNameAuditEntries` (`AuditLog.cs`, `AuditLogEntryConfiguration.cs`, `AppDbContext.cs` and `Migrations/`, `AuditLogRulesTests.cs:87-89`) fails on `new AuditLogEntry(…)`, a target-typed `new(…)` assigned to an `AuditLogEntry`, or a `Set<AuditLogEntry>()` followed by any invocation whose name starts with `Add`, `Attach` or `Update` (so `AddRange`, `AddAsync`, `AddRangeAsync`, `AttachRange`, `UpdateRange` too)

5. **The remaining C# rules read syntax too** (TST-2; R34)
   **Given** the using-alias rule (`UsingAliasRulesTests.SourceAndTests_UsingAliasDirective_IsAbsent`), the null-forgiving justification rule (`SourceRulesTests.ProductionSource_NullForgivingJustificationComment_IsAbsent`), the error-code rule (`ProblemCatalogueTests.ErrorCodes_DomainAndApplication_AreAllInTheCatalogue`) and `PlatformScriptTests.ApiProgram_ForwardedHeaders_RunFirstInThePipeline` (a regex over `Program.cs` today)
   **When** they run
   **Then** the first finds `UsingDirectiveSyntax` with an `Alias`; the second finds single-line comment trivia starting with `!`; the third reads the first argument of each `Error.<Kind>(…)` invocation; the fourth finds, after the `builder.Build()` call in `Api/Program.cs`, that the first invocation on `app` whose name starts with `Use` is `UseForwardedHeaders`; the MSBuild check (already `XDocument`) and the markdown `StatusLine` check (not C#) stay as they are

6. **The regex self-tests go, and each rule keeps a positive check on the tree** (TST-2; R35)
   **Given** the `[InlineData]` sample-source theories whose subject is a regex over C# (`AutomationActorTypeLines_SampleSource_…`, `ParameterlessIgnoreQueryFilters_SampleLine_…`, `OperationsOrSlaNamespace_SampleLine_…`, `IgnoreQueryFilters_SampleLine_…`, `BulkOrRawSqlCall_SampleLine_…`, `NamesInsertOnlyRecords_SampleText_…`, `ConstructsOrAddsAuditEntry_SampleLine_…`, `UsingAliasDirective_SampleLine_…`, `ErrorFactoryCall_SampleLine_…`)
   **When** this story lands
   **Then** they are deleted with their `[GeneratedRegex]` members (and `FirstMiddleware` in the backend `PlatformScriptTests`); each rule keeps (or gains) one positive check on the real tree that proves its scan finds an allowed use (as `ProductionSource_AutomationActorType_IsFoundInTheSweep` does today); that each rule rejects a forbidden use is proved by the Supervisor checks below, each an acceptance check of this story

7. **Banned APIs fail the build** (TST-2, wave 4 item 1; R25, R35)
   **Given** `Microsoft.CodeAnalysis.BannedApiAnalyzers` referenced through `backend/Directory.Build.props` (or the test and source `Directory.Build.props`) with one `BannedSymbols.txt` for the production projects and one for the test projects
   **When** a project references a banned symbol
   **Then** the build fails with RS0030 (warnings are errors): production projects ban every overload of both `AddDbContextPool` and `AddPooledDbContextFactory` (each overload's documentation ID, generic arities included, listed from the pinned EF Core version, since the format has no all-overloads form), replacing `ProductionSource_AddDbContextPool_IsAbsent` (`OwnershipRulesTests.cs:95-103`, which bans both today), which is deleted; test projects ban `Microsoft.Extensions.Logging.ILoggerProvider` (8.4 replaced both captures with `FakeLogCollector`); each line carries its reason after `;`

## Boundaries

- Must not change: what any rule allows or forbids; `LayerTests`; any production file; the allowed folders and files of each rule.
- Rules that read non-C# files (MSBuild items, problem pages) keep their current mechanism.
- `MeterListener` is not banned: `StoredEventPrivacyTests`' factory-scoped recorder stays (8.4, criterion 1).

## Supervisor checks

Throwaway copy, each must fail naming the file and line (or the build with RS0030):
- `ActorType . Automation` with a line break after the dot in an Api file.
- `DisputeDesk.Application.Operations.SomeType.Member` written as an expression in `Application/Disputes/`.
- `IgnoreQueryFilters(["Ownership"])` in `Application/Disputes/`.
- `ExecuteSqlRaw` in a file that names `StoredEventRow`; `ExecuteSqlAsync($"DELETE FROM \"AuditLogEntries\"")` in a file whose only mention is that SQL text.
- `db.Set<AuditLogEntry>().AddRange(entries)` and `.AddAsync(entry)` in an Application file.
- A string literal `"// !"` in a production file: the null-forgiving rule does not fail (the 5.3 escape).
- `app.UseExceptionHandler();` moved above `app.UseForwardedHeaders();` in `Program.cs`.
- `services.AddDbContextPool<AppDbContext>(…)` and `services.AddPooledDbContextFactory<AppDbContext>(…)` in Infrastructure: the build fails with RS0030 for each.
- A class implementing `ILoggerProvider` in an integration test: the build fails with RS0030. If the analyzer does not flag a base-list use, the builder reports it and names the uses it does flag (the analyzer's documentation does not say).

## Build facts

- Owner: backend (backend-builder).
- Tier: Small, test-only (plus analyzer configuration).
- Flags: none.
- Lane: A, after 8.4.
- Mechanisms and sources:
  - Roslyn syntax analysis: <https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/get-started/syntax-analysis>.
  - BannedApiAnalyzers, `BannedSymbols.txt` (one documentation ID per line, `;` then the reason): <https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.BannedApiAnalyzers/BannedApiAnalyzers.Help.md>; documentation ID format: <https://learn.microsoft.com/dotnet/csharp/language-reference/language-specification/documentation-comments#d42-id-string-format>.
- Packages (both from Microsoft on nuget.org, latest stable on 2026-11-04; the builder re-checks per AGENTS.md, Dependency verification, and pins exactly). SDK 10.0.403 ships Roslyn 5.9.0-1, so both load (pre-build review):
  - `Microsoft.CodeAnalysis.CSharp` 5.9.0, referenced by the ArchitectureTests project only.
  - `Microsoft.CodeAnalysis.BannedApiAnalyzers` 5.6.0, as a `PrivateAssets="all"` analyzer.
- Touches: `backend/Directory.Packages.props`, `backend/tests/DisputeDesk.ArchitectureTests/*.cs` (the `ApiProgram_ForwardedHeaders_…` case of `PlatformScriptTests.cs` included) and `.csproj`, `backend/Directory.Build.props` or `backend/tests/Directory.Build.props`, two `BannedSymbols.txt` files (new).
- Collision: 8.7 rewrites the `Compose_*` cases of the same backend `PlatformScriptTests.cs`, in other methods; 8.5 merges first (lane A order) and 8.7 rebases onto it. 8.15 later adds the seeder folder to the ownership rule's allowed folders.

## Pre-build

- Challenger: `docs/verification/8.5-challenge.md` (simplify).
- Review: `docs/verification/epic-8-prebuild-wave2.md`, section 8.5. No security read.
- Decisions: none raised.
- Sofia, 2026-11-04: every `[story]` point folded. Review: both pooled registrations banned, every overload (criterion 7, Supervisor checks); `ApiProgram_ForwardedHeaders_RunFirstInThePipeline` in scope (criteria 1, 5; Collision with 8.7); the identifier prefix restated (criterion 4). Challenger: both pooled APIs (criterion 7); SQL table names in strings still found (criterion 4); `MayNameAuditEntries` and the other predicates kept (criteria 1, 4); the test-project ban waits on 8.4 (`after`), and `MeterListener` is not banned because 8.4 keeps one factory-scoped recorder (criterion 7, Boundaries); member-access forms covered and the rule stays syntactic (criterion 2); the `Add`/`Attach`/`Update` variants (criterion 4); negative mutations are acceptance checks (criterion 6, Supervisor checks); one parse per file, checked in review (criterion 1).

## References

- parent — docs/tickets/epic-8-simplicity-sweep/epic-8-simplicity-sweep.md, section Requirements
- audit — docs/verification/simplicity-audit/tests.md, TST-2
- plan — docs/verification/simplicity-audit/mitigation-plan.md, wave 2, S4; wave 4, item 1
- verification — docs/verification/flaky-fixes.md (the 5.3 comment-blanking escape)
- architecture — docs/architecture/architecture.md, AD-3, AD-11, AD-14, AD-14a

## Notes

- Risk low (proposed): test and build configuration only; each rule's Supervisor check proves it still fails.
- Decision: 2026-11-04 (Sofia) — both packages are used, each for its own kind of rule: the folder rules (allowed in `Operations/`, not in `Disputes/` of the same project) cannot be expressed as a per-project ban, so they need the syntax tree; the per-project bans need no test code, so they are BannedApiAnalyzers, as the plan prefers.
- Decision: 2026-11-04 (Sofia) — the namespace rule stays syntactic (the challenger's second option): it matches the project's own namespace names as today, and a semantic model would need a compilation of every project for no rule change.
- Decision: 2026-11-04 (Sofia) — `MeterListener` leaves the ban list, following 8.4's decision to keep `StoredEventPrivacyTests`' recorder; banning it would need a suppression, which AGENTS.md forbids.
