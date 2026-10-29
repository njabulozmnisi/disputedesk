# Naming the code that turns a dispute and its stored events into the customer's response

Question: in event-sourced and CQRS .NET systems, what is the established name for code that turns an aggregate's state and/or its stored events into a read-side shape, and what should `CustomerView` (`backend/src/DisputeDesk.Application/Disputes/CustomerView.cs`) be called? Researched 2026-10-29.

## What the code does

`CustomerView` is a static class with two public methods. `ToResponse(Dispute, IEnumerable<StoredEvent>)` returns the API DTO `DisputeResponse`. `Timeline(storedEvents)` builds the customer timeline through a deny-by-default allowlist of five stored event types. It runs at read time and to build the raise 201 body. It persists nothing. The persisted read model is the `Disputes` row, which the AD-14 commit writes in the same transaction as each stored event (AD-13, AD-22).

## Short answer

There is no single standard name for this kind of code. The literature agrees on two things:

1. The persisted read-side shape is a **read model**, **materialized view** or **view**.
2. The code that keeps a read model up to date from events is a **projection**, also called a projector, denormalizer or event handler.

A read-time mapping that persists nothing has only library-specific names: Marten's **live projection** or **live aggregation**, and the DTO or **ViewModel** mapping of the .NET microservices guide. "Projection" alone would mislead a reader of this repo. Microsoft Learn defines a materialized view as "a read model or projection that **persists** data", and the repo already has a persisted read model (`Disputes`) built by the commit, not by this class.

**Recommendation: keep `CustomerView`.** It names what the class produces: the dispute as its customer sees it. That is the sense "view" has in CQRS (a read-only view of the system state, shaped for one consumer). It matches the term the architecture and stories already use ("the customer view", "Customer wire view", AD-13), and renaming it costs nothing. One small change is worth making: rename the method `Timeline` to `ToTimeline`. Method names should be verbs (.NET Framework Design Guidelines), and it then matches `ToResponse`, `ToTimelineEntry` and `ToMoney` in the same class.

Runner-up: `CustomerResponseMapping` (or `CustomerResponses`). It is the most literal name, "maps a dispute and its stored events to the customer's response". But it hides the allowlist, which is the security-relevant part, and it would split the vocabulary from AD-13.

## Findings by source

### Microsoft Learn

- **Event Sourcing pattern.** "Materialized views are read-only projections of the event store that are optimized for querying ... a handler updates the materialized view." Current state is derived by replaying a stream, called *rehydration*, "on demand when the application handles a request." The pattern uses *projection* and *materialized view* for derived data that is maintained over time. ([Event Sourcing pattern](https://learn.microsoft.com/azure/architecture/patterns/event-sourcing))
- **Materialized View pattern.** "A *materialized view* is a read model or projection that persists data derived from one or more source stores. A dedicated application component ... can maintain the projection." The pattern also lists "Access control on views. When a materialized view is used to restrict which data subsets are visible to certain consumers ... your refresh pipeline must exclude unintended columns or rows". In this vocabulary, an audience filter is a property of a view. ([Materialized View pattern](https://learn.microsoft.com/azure/architecture/patterns/materialized-view))
- **CQRS pattern.** "The *read model* generates materialized views from these events ... tailoring structures to query and display requirements." ([CQRS pattern](https://learn.microsoft.com/azure/architecture/patterns/cqrs#combine-the-event-sourcing-and-cqrs-patterns))
- **.NET Microservices Architecture (eShopOnContainers).** The data returned to clients is a DTO: "These models, or Data Transfer Objects (DTOs), are called ViewModels." The ordering service returns ViewModel DTOs such as `OrderSummary` from query classes (`OrderQueries`). The guide names the output type and the query class, not a mapper. ([Implement reads/queries in a CQRS microservice](https://learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/cqrs-microservice-reads))
- **Cloud-native data patterns.** "The system projects the current state by replaying all the events ... This view, or read model, is then exposed back to the UI." *View* and *read model* are used as synonyms. ([Cloud-native data patterns](https://learn.microsoft.com/dotnet/architecture/cloud-native/distributed-data#high-volume-data))
- **Framework Design Guidelines.** "DO name classes and structs with nouns or noun phrases." "DO give methods names that are verbs or verb phrases." ([Names of classes, structs and interfaces](https://learn.microsoft.com/dotnet/standard/design-guidelines/names-of-classes-structs-and-interfaces); [Names of type members](https://learn.microsoft.com/dotnet/standard/design-guidelines/names-of-type-members))
- **CQRS Journey (patterns & practices, archived).** "The read side is a simpler, read-only view of the system state accessed through queries", and the read side updates "the data that is used by its queries and views." ([Reference 2: Introducing the CQRS pattern](https://learn.microsoft.com/previous-versions/msp-n-p/jj591573(v=pandp.10)))

### Event-sourcing literature and libraries

- **Greg Young, CQRS Documents.** The query side is a "Thin Read Layer" that "reads directly from the database and projects DTOs". Here *project* is a verb meaning "shape into a DTO at read time", not "persist". Seen only through secondary excerpts ([Gojko Adzic's write-up of the 2010 talk](https://gojko.net/2010/06/11/evolution-of-ddd-cqrs-and-event-sourcing)); the original PDF was not retrieved.
- **Martin Fowler.** *CQRS* uses "query model", "read models" and "query-side models", and mentions "view" only as an analogy to relational views ([CQRS](https://martinfowler.com/bliki/CQRS.html)). *Event Sourcing* calls derived state "application state" and rebuilding it a "Complete Rebuild". It never uses "projection" or "read model" ([Event Sourcing](https://martinfowler.com/eaaDev/EventSourcing.html)).
- **Vaughn Vernon, Implementing Domain-Driven Design.** "Read Model Projections" are "a simple set of Domain Event subscribers that are used to generate and update a persistent Read Model". There is also a section on "an Eventually Consistent Query Model". Only the preview was seen. ([O'Reilly preview](https://www.oreilly.com/library/view/implementing-domain-driven-design/9780133039900/app01lev1sec11.html))
- **Oskar Dudycz (event-driven.io).** A projection is "a different interpretation of the set of facts". "Most of the time, we store the result as a materialised read model", though "the projection result can be anything ... in-memory data". In this account the projection is the process and the read model is the usual result. ([Projections and read models in event-driven architecture](https://event-driven.io/en/projections_and_read_models_in_event_driven_architecture))
- **Marten.** This is the only source that names read-time, non-persisted projection explicitly. **Live** projections "are executed on demand by loading event data and creating the projected view in memory" "without persisting the projected documents". **Inline** projections run "in the same unit of work" as event capture. **Async** projections run in a background process. Live aggregation is `AggregateStreamAsync<T>`. Projection classes are named `<Thing>Projection` (`AppointmentDurationProjection`), and self-aggregating documents are named for the read model itself (`QuestParty`). ([Marten projections](https://martendb.io/events/projections/); [Live aggregates](https://martendb.io/events/projections/live-aggregates))
- **Event Modeling (Adam Dymitruk).** A *state view* slice takes events into a **read model** that a screen consumes ("Event(s) → Read Model → Screen"). The read model is "a purpose-built view, shaped for what a specific screen needs". eventmodeling.org itself was not retrieved; this is from secondary descriptions ([Axoniq, Event Modeling](https://www.axoniq.io/concepts/event-modeling); [Mermaid event modeling syntax](https://mermaid.js.org/syntax/eventmodeling.html)).
- **Axon.** Queries are "typically answered by a 'projection' or 'view model'", and its event-handling components are *projectors* that build a *query model*. ([Axon query handling](https://docs.axoniq.io/reference-guide/4.0/implementing-domain-logic/query-handling); [Projectors migration guide](https://docs.axoniq.io/axon-framework-reference/5.1/migration/paths/projectors-event-processors/); [Creating the query model](https://docs.axoniq.io/bikerental-demo/main/create-bike-status-projection))
- **KurrentDB (EventStoreDB).** Server-side "projections" are JavaScript functions that emit or link events into new streams. Read models are built in external stores by subscriptions. In KurrentDB, a projection produces streams, not DTOs. ([KurrentDB projections](https://docs.kurrent.io/server/v26.1/features/projections/system.html); [Mix-and-match database tutorial](https://docs.kurrent.io/dev-center/use-cases/mix-and-match-database/tutorial/tutorial-intro))
- **Eventuous.** "Read models are projections of the system state, which are built based on the query needs". The code that writes them is a *projector* (`MongoProjector<T>`). Seen through a staging mirror only. ([Eventuous read model concept](https://0e5ca858.eventuous-main.pages.dev/read-models/rm-concept))

### Summary of what each source calls the four things

| | (a) persisted read model | (b) code that builds it from events | (c) read-time, non-persisted projection | (d) state to API DTO mapper |
|---|---|---|---|---|
| Microsoft Learn (Azure patterns) | read model, materialized view, projection | handler, "dedicated component" | rehydration (for the aggregate) | not named |
| eShopOnContainers guide | (none; queries read tables) | (n/a) | (n/a) | ViewModel (the DTO), queries class |
| Greg Young | read model | event handlers / denormalizer (community) | thin read layer "projects DTOs" | thin read layer |
| Fowler | query model, read model, reporting database | event handlers | application state, complete rebuild | not named |
| Vernon | read model, query model | read model projections (event subscribers) | not named | not named |
| Dudycz | read model | projection | not named in the article | not named |
| Marten | projected document | projection (`SingleStreamProjection` and others), inline or async | **live projection**, live aggregation | not named |
| Event Modeling | read model (view) | state view slice | not distinguished | not named |
| Axon | query model | projector | not named | view model |
| KurrentDB | external read model | subscription; server "projections" emit streams | not named | not named |
| Eventuous | read model | projector | not named | not named |

None of the sources gives a standard name for (d). Every source that names (b) uses projection or projector, and every one of them except Marten's *live* mode means a component that writes something.

## Term definitions

- **Read model / query model.** The data shape the read side serves, usually persisted and denormalized (Microsoft Learn CQRS; Fowler; Vernon). Here it is the `Disputes` row.
- **Materialized view.** A read model that is persisted and maintained from source data, rebuildable and read-only to consumers (Microsoft Learn, Materialized View pattern). The `Disputes` row is one; AD-13 already cites it so.
- **View.** Loosely, a read-only shape of the system state for one consumer or screen (CQRS Journey; Event Modeling; the cloud-native guide uses it as a synonym of read model). Persistence is not implied.
- **Projection.** The process, or the component, that folds events into a read model (Vernon, Dudycz, Marten, Axon, Eventuous). It is persisted by default. Marten's *live* projection is the exception, and the qualifier *live* is what signals that.
- **Projector.** The component that runs a projection (Axon, Eventuous).
- **View model.** The DTO returned to a client for a query (eShopOnContainers, Axon).
- **Mapper / DTO mapping.** Code that copies a domain object into a DTO. It is a general-purpose term with no event-sourcing meaning.

**Which honestly describes `CustomerView`?** It folds stored events into the timeline, so in a narrow sense it is a projection. It is not persisted and not maintained over time, so it is at most a *live* projection in Marten's vocabulary. It also maps the replayed state to the API DTO, which is view-model mapping, and it applies an audience filter, which the Materialized View pattern treats as a view concern. "View" (the customer's view of a dispute) is the only term that covers all three without claiming persistence.

**Would "Projection" mislead?** Yes, here. The repo already has a persisted read model written by the commit (AD-14), and Microsoft Learn, which AD-13 cites, defines a projection as something that persists. A reader seeing `CustomerProjection` would look for where it writes, or assume it maintains the `Disputes` row.

## Recommendation

**Keep `CustomerView`.** One-sentence explanation for a new developer: "`CustomerView` builds the dispute as its customer sees it, the response and the allowlisted timeline, from the replayed dispute and its stored events; it writes nothing."

Reasons:

1. It is a noun phrase naming what the class produces (Framework Design Guidelines). It sits beside `DisputeWire` (the wire side of the enums) and `DisputeReplay` (loading by replay), which are named the same way, after their role.
2. "View" is the established CQRS word for a read-only shape for one consumer (CQRS Journey, Event Modeling, the cloud-native guide) and does not claim persistence.
3. The term is already the repo's: AD-13 ("the customer view passes them through one allowlist", "Customer wire view, mapped once in Application"), story 3.3 ("story 3.8's customer view mapping"), story 3.4 ("the updated customer view of the dispute") and `docs/initiative-disputedesk.md`. Renaming the class would split the code from its documents.
4. It leaves an obvious counterpart for FR-30 (operations see every type) without inventing a new pattern name.

It is not a glossary term, and needs none: it is a code name for the AD-13 concept, not a domain term a customer or operator uses. Do not add "projection" to the glossary either. AD-13's word "read model" for the `Disputes` row already matches the literature.

**Method names.**

- `ToResponse`: keep. A verb phrase following the `ToX` conversion convention (`ToString`, `ToArray`).
- `Timeline`: rename to **`ToTimeline`**. It is the only public method named with a noun (Framework Design Guidelines: "DO give methods names that are verbs or verb phrases"), and it would match `ToResponse`, `ToTimelineEntry` and `ToMoney` in the same file.

**Runner-up: `CustomerResponseMapping`.** It is the most literal name and needs no CQRS vocabulary. Choose it only if "view" is judged ambiguous (an MVC view or a database view). It loses the link to AD-13's wording and does not hint at the allowlist.

**Rejected:** `CustomerProjection` or `DisputeProjection` (they imply persistence; see above); `CustomerReadModel` (the read model is the `Disputes` row); `CustomerViewModel` (in .NET that names the DTO, here `DisputeResponse`, not the code that builds it); `CustomerDisputeMapper` (a `Mapper` suffix suggests AutoMapper-style infrastructure, which AGENTS.md excludes).

## Rename cost

**Recommended change (`Timeline` to `ToTimeline`) only:**

- `backend/src/DisputeDesk.Application/Disputes/CustomerView.cs`: the declaration and its one call inside `ToResponse`.
- `backend/tests/DisputeDesk.Api.IntegrationTests/Disputes/CustomerTimelineAllowlistTests.cs`: 4 calls.
- `backend/tests/DisputeDesk.Api.IntegrationTests/Disputes/StoredEventReplayTests.cs`: 1 call.

No document names the method. This is a trivial-tier change, with one build and a run of the two test classes.

**If the class were renamed (runner-up):**

- Code: `CustomerView.cs` (file and class), `RaiseDispute/RaiseDisputeHandler.cs` (1 call), `CustomerTimelineAllowlistTests.cs` (4), `StoredEventReplayTests.cs` (1).
- Stories: `docs/tickets/epic-3-disputes/tickets/tickets.toml` (the 3.8 and 3.3 `touches` lines), `story-3.8-...md` (lines 145, 167), `story-3.3-...md` (line 77).
- Architecture: AD-13's prose "customer view" and "Customer wire view" could stay as a concept, but would then name nothing in code. For consistency, reword it, and the matching phrases in story 3.3 (line 16), story 3.4 (line 96) and `docs/initiative-disputedesk.md` (line 130).
- Historical records (`docs/verification/brief-es-chain-tickets.md`, `3.8-challenge.md`, `brief-es-rewording-rules.md`): leave them as written.

## Open points

- Story 3.3's `touches` lists `Application/Disputes/... CustomerView/` as a folder, but it is a file (`CustomerView.cs`). Correct the story when 3.3 is next refined.
- When FR-30's operations reads are built, the same naming rule gives `OperationsView` beside `CustomerView`. Whether operations need a separate mapping, rather than returning stored events with the internal status, is for that story to decide.
- Not retrieved first-hand: Greg Young's CQRS Documents PDF, eventmodeling.org, and the official Eventuous site (a staging mirror was used). Their wording above comes from secondary excerpts and does not change the conclusion.
