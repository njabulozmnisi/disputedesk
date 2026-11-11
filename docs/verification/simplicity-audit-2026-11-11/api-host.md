# Simplicity and standards audit, 2026-11-11: api-host

Set: `backend/src/DisputeDesk.Api`, `backend/src/DisputeDesk.Contracts`, `backend/src/DisputeDesk.Worker`, `backend/stubs`,
`backend/Directory.Build.props`, `backend/Directory.Packages.props`, `backend/BannedSymbols.txt`, `backend/Dockerfile`.
Tree: the lows-2026-11-10 worktree at `88b28c3` (after the lows batch's own changes, so these counts include them).
Rules and scale: `~/disputedesk-rename/retro-8/audit-rerun-plan.md` (AGENTS.md "How to work", `docs/conventions/backend.md`,
review-story pass 2 and its standards check). Read-only; nothing was built or run.

## Findings

| ID | Severity | Tag | File:line | Finding | Simplest fix |
|---|---|---|---|---|---|
| API--1 | medium | [code] | `Api/Linking/LinkingEndpoints.cs:16`, `Api/Program.cs:62-63` | `AddLinkingEndpoints` only forwards to `AddLinkingRateLimits`: a wrapper that adds a second `Add*` call for the linking group and a name that hides what it registers. AGENTS.md says no wrapper that only forwards calls. (The baseline's "one `MapLinking`" closed on the map side; the registration side still has two entry points.) | Delete `AddLinkingEndpoints`. Call `AddLinkingRateLimits()` from `AddLinkingRoutes` (or straight from Program). |
| API--2 | low | [code] | `Api/Authentication/AuthenticationEndpoints.cs:45,59,91`; `Api/Linking/LinkingEndpoints.cs:52,75`; `Api/Linking/RateLimiting/LinkingRateLimits.cs:97`; `Api/Authentication/SessionTicketStore.cs:39`; `Api/Authentication/WebAppAuthentication.cs:193`; `Api/Authorization/LinkedCustomerHandler.cs:20`; `Api/Authorization/UnlinkedLoginHandler.cs:17`; `Api/Operations/OperationsActorHandler.cs:22`; `Api/Authorization/AuthorizationResultHandler.cs:71` | The `sub` claim is read in 12 places with a literal `"sub"` and three fallbacks: null, `""`, or a throw whose message is copied five times. The same `IsAuthenticated ? FindFirst("sub") : null` line appears three times in one file. That is well past the third real case. | Add one `ClaimsPrincipal` extension pair, for example `LoginSubOrNull()` and `RequiredLoginSub()`, keyed on `JwtRegisteredClaimNames.Sub`, and use it at every site. |
| API--3 | low | [code] | `Api/Disputes/DisputeActionMetrics.cs:54` | The `outcome` label is hand-mapped (`outcome == Outcome.Upheld ? "upheld" : "rejected"`). The same class (line 47) and `RaiseMetrics.cs:25` derive their labels from the enum name. This is what is left of the baseline's "metric label from the enum" tidy, and a new `Outcome` member would be labelled `rejected`. | `JsonNamingPolicy.CamelCase.ConvertName(outcome.ToString())`, as on line 47. |
| API--4 | low | [code] | `Contracts/Operations/OperationsRoutes.cs:4`; `Api/Operations/OperationsEndpoints.cs:34`; `Contracts/Disputes/DisputeRoutes.cs:13,19`; `Api/Disputes/DisputeEndpoints.cs:41,54` | Route constants that unify nothing. `OperationsRoutes` has one consumer, and the read route right beside its uses is a literal. `DisputeRoutes.List` has one consumer, and the raise maps the same `"/disputes"` as a literal. `DisputeRoutes.DetailPath` repeats the detail pattern and the `/api/v1` prefix instead of building on `Detail` and `ApiV1`, so the class comment's "in one place" is not true. Only `DetailPath` has a second consumer (the raise's `Location`). | Inline the operations routes and `List`/`Withdraw` as literals at `MapPost`/`MapGet`, and delete `OperationsRoutes`. Keep `DisputeRoutes.Detail` and `DetailPath`, and correct the class comment. The `Location` integration test already guards the pairing. |
| API--5 | low | [code] | `backend/Directory.Packages.props:39` | An orphan comment: "ValidateDataAnnotations for the Worker's options; the Api gets it from the ASP.NET Core shared framework." It sits above `Npgsql.EntityFrameworkCore.PostgreSQL` and describes a package that was removed (`Microsoft.Extensions.Options.DataAnnotations`, last touched in `ef88282`). Nothing in the Worker calls `ValidateDataAnnotations`. | Delete the line. |
| API--6 | low | [code] | `Api/Customers/CustomerRoutes.cs:22-23` | The single `IAuthorizationMiddlewareResultHandler` is registered inside `AddCustomerRoutes`, but the linking 403 and the operations `Denied` audit (AD-14a) depend on it. A reader of `OperationsAuthentication` cannot find it, and moving or dropping the customer routes would silently stop the operations audit. | Register it where the authorization defaults are set, beside the fallback policy in `AddWebAppAuthentication` (`WebAppAuthentication.cs:82-83`), with the same comment. |
| API--7 | low | [code] | `Api/Transactions/TransactionEndpoints.cs:39` vs `Api/Disputes/DisputeEndpoints.cs:86` | The two list endpoints bound their `cursor` input in two different ways. Disputes uses built-in minimal API validation (`[MaxLength]` on the parameter). Transactions has no attribute and relies on `TransactionCursor.Decode`'s length check in Application. AGENTS.md and the conventions put bounds at the edge with built-in validation. | Put `[MaxLength(TransactionCursor.MaxLength)]` on the transactions `cursor` parameter, as on the disputes list. Keep the decode check for well-formedness only. |

Checked and not counted:
- `IdempotencyKeyHeader.Parse`: the .NET runtime has no RFC 9651 parser, and `HeaderUtilities` follows RFC 9110 quoted-string, which accepts obs-text and tabs. The parser cites its standard.
- `LinkingRateLimitMetadata`: a chained global limiter needs its own endpoint test. `RequireRateLimiting` would make the middleware look up a named policy that cannot express two chained partitions.
- `SessionTicketStore` uses the documented `ITicketStore` extension point.
- `BuildTimeOpenApi` follows the documented build-time switch.
- `EnumSchemaTransformer` follows from AD-5's custom converter.
- `ClaimedIdempotencyKey` uses the same scoped-holder pattern as `CurrentCustomer`.
- The `RenderAsync` body swap is commented and has one purpose.
- `DatabaseMigrator` as both hosted service and health check is documented.
- The Worker host, the core-banking stub, the Dockerfile, `Directory.Build.props` and `BannedSymbols.txt` raised no finding.

## Counts

| Severity | 2026-11-03 baseline | 2026-11-11 |
|---|---|---|
| High | 0 | 0 |
| Medium | 2 | 1 |
| Low | 4 | 6 |
| Total | 6 | 7 |

Fate of the baseline findings:
- API-1 (D5, one enum rule) is closed: `SingleNameEnumConverter` is gone, and the factory creates `CamelCaseNameOnlyEnumConverter` for every enum.
- API-2 (D4, the `Operations:Actors` map) is closed: `Operations:AllowedClients` is a plain `sub` array.
- API-3 to API-6 (the 8.2 tidy) look closed: one `MapLinking`, enum-derived metric labels, the security-headers rename. Two partial leftovers are counted as new: API--1 on the registration side and API--3 on the one hand-mapped label.
- The baseline report itself is no longer in the repo, so API-3 to API-6 are matched by the slice description in the mitigation plan, not by their original text.

## Trend

Both baseline mediums were architecture-level and both are closed by their decisions (D4, D5). No high finding appeared. The one remaining medium is a single forwarding method. The low count rose from 4 to 6 because this pass counted small duplication and dead-indirection points (the `sub` reads, route constants, an orphan comment) that the baseline either did not reach or that grew in later stories; none of them hides a defect.
