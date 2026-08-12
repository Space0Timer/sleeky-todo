# Architecture

The application uses a layered monolith:

```text
React
  -> ASP.NET Core API
  -> Application commands and queries
  -> Domain and infrastructure
  -> MongoDB
```

The Application layer owns persistence and time abstractions. Infrastructure supplies their runtime implementations, keeping command handlers testable and independent of MongoDB and the system clock.

## React client and persisted workflow

The React client uses a typed API module for list, detail, create, update,
status, dependency, delete, and restore requests. It parses Problem Details in
one place and distinguishes validation, domain-rule, concurrency, not-found,
network, and unexpected failures. Serilog and backend implementation types do
not cross the HTTP boundary.

The main screen is backed by `GET /api/todos`; it does not keep a browser-only
TODO collection. Active, Archived, and Trash tabs select the matching server
scope. Changing scope, filters, sort field, or direction starts a new first-page
request without a cursor and replaces the displayed items. Load More sends the
opaque `nextCursor` and appends the returned page.

List responses remain projections. Full TODO details are loaded only when the
user opens management controls or when a blocked card needs prerequisite names.
Each mutation of an existing TODO uses the version from the latest loaded
representation and then refreshes the persisted list. A concurrency response is
never overwritten silently: the UI displays the conflict and Reload Latest
Version replaces stale state from the server.

Dependency selection is deliberately bounded. The client requests one active
page of at most 100 TODOs sorted by normalized name and searches only within
that loaded set, excluding the current TODO and already-selected dependencies.
This avoids loading an unbounded collection, but it also means TODOs beyond the
first 100 candidates cannot currently be selected. A server-side dependency
search endpoint is required before removing that limitation.

## Persistence boundary

Application handlers depend on the public `ITodoRepository` abstraction. Infrastructure registers `MongoTodoRepository` as its implementation:

```text
Application handler
  -> ITodoRepository
  -> MongoTodoRepository
  -> IMongoDatabase / IMongoCollection<TodoDocument>
  -> MongoDB
```

`TodoDocument`, its serializer, and its mapper are internal Infrastructure details. They are not exposed to Application or test projects. Repository integration tests exercise the public `ITodoRepository` contract and inspect raw BSON only when the persisted representation matters.

A separate `MongoDbContext` wrapper is intentionally not used. With one database and one collection it only duplicated `IMongoDatabase.GetCollection`; the repository owns collection access directly.

List queries use a separate `ITodoListReader` implemented by
`MongoTodoListReader`. Its aggregation applies scope and field filters, joins
dependency documents, computes blocked state, applies the deterministic cursor,
and fetches `limit + 1`. Application owns cursor encoding and validation so the
HTTP and persistence layers share one filter-bound cursor contract without
exposing BSON types.

## Optimistic concurrency

Every mutable TODO carries a numeric version. Update, soft-delete, restore,
dependency, and status requests include the version last read by the client.
Backend-owned TODO, dependency, recurrence-series, and cursor tie-breaker
identifiers use `Guid` throughout Domain, Application, API, and Infrastructure.
MongoDB stores them as standard BSON UUIDs (binary subtype 4), including TODO
IDs, dependency IDs, recurrence-series IDs, and cursor tie-breaker IDs. The
JSON/React contract remains unchanged because ASP.NET Core represents each
`Guid` as its canonical UUID string at the HTTP boundary. Persistence document
models ignore unknown BSON elements to support additive rolling schema changes.

The repository performs each mutation as one MongoDB `FindOneAndReplace` operation with a filter equivalent to:

```text
_id == todoId AND version == expectedVersion
```

Update and soft-delete also require an active document. Restore requires a deleted document. The replacement increments the version by one, and `ReturnDocument.After` returns the actual persisted state.

If the filter matches nothing, the repository returns `null`; command handlers map that result to `ConcurrencyConflictException`. This covers the race between the handler's initial read and its write. `UpdatedAt` remains ordinary data and is never used as the concurrency token.

Integration tests issue simultaneous mutations with the same version and verify that exactly one succeeds for update/update, update/delete, and restore/restore races.

## Transactional recurring completion

Only a real transition from a non-completed state into `Completed` raises a
`TodoCompletedDomainEvent`. Completion runs through a scoped transaction
coordinator:

```text
ChangeTodoStatus handler
  -> start MongoDB session transaction
  -> versioned replacement of current occurrence
  -> dispatch TodoCompletedDomainEvent in-process
  -> calculate next date from scheduled due date
  -> insert next occurrence through the same session
  -> commit
```

The MongoDB repository reads the scoped transaction context and uses the active
session for both the replacement and event-handler insert. Any event-handler or
write failure aborts the transaction. A unique partial index on series ID and
occurrence number complements optimistic concurrency and prevents duplicate
next occurrences.

The recurrence calculator preserves a stored monthly anchor rather than adding
months to a previously clamped date. Thus January 31 becomes February's final
day and then March 31. New occurrences copy descriptive fields, priority, and
the schedule, but deliberately start with no dependency IDs.

## Soft delete and restore

Soft delete is a domain transition rather than a physical MongoDB delete:

```text
Active TODO
  -> deletedAt = current UTC time
  -> purgeAt = deletedAt + 90 days
  -> updatedAt = deletedAt
  -> version = version + 1
```

Normal reads and existence checks add `deletedAt == null` to their repository filter. Restore deliberately includes deleted records, validates that the retention boundary has not been reached, clears `deletedAt` and `purgeAt`, and persists the next version atomically.

Deletion state is valid only when `deletedAt` and `purgeAt` are either both null or both present, with `purgeAt` later than `deletedAt`. Restore cannot use a timestamp before deletion.

The background job that physically removes records after `purgeAt` is separate
from this recoverable lifecycle. Before deletion, the application asks the
repository whether an active, non-archived TODO depends on the target and
rejects the transition when one exists.

## Dependency graph and status rules

Dependency mutations are application commands backed by aggregate methods on
`TodoItem`. The add path verifies an active target, then uses a breadth-first
graph service to determine whether the proposed target already reaches the
source. Each frontier is loaded with one `GetByIdsAsync` call, and a visited set
guarantees termination for malformed legacy graphs.

Status transitions use a dependency evaluator that batch-loads all direct
dependencies, including deleted documents. Missing, deleted, archived, or
non-completed dependencies contribute to the incomplete count and block entry
to `InProgress` or `Completed`. The list reader independently projects the same
blocked semantics for query responses and filtering.

All dependency and status writes retain the source TODO's expected version in
the repository filter. A graph check is therefore followed by the same atomic
optimistic write used by the CRUD commands.

## HTTP and error boundary

Controllers translate API contracts into MediatR requests and select success
status codes; they contain no domain or persistence behavior. FluentValidation
runs before handlers, and a domain-rule pipeline behavior converts domain
exceptions into the application-facing `DomainRuleException`.

The global API exception handler produces RFC Problem Details. It maps
`NotFoundException` to 404 and both `ConcurrencyConflictException` and
`DomainRuleException` to 409. Validation errors use a predictable camel-cased
`errors` dictionary. All problem responses include the request path and a
`traceId`.

## Logging boundary

Serilog is configured only by the API host. API, Application, and Infrastructure
classes emit events through Microsoft `ILogger<T>`, which supplies a stable
source category while keeping those layers independent of the logging provider.
The host uses a bootstrap logger for startup failures and replaces it with the
configuration-driven Serilog pipeline after dependency injection is available.

One HTTP completion event records method, path, status, duration, request ID,
and trace ID. A MediatR behavior records the application request type and
successful handling duration, and Infrastructure records successful MongoDB
index initialization. Successful health-check completion events are reduced to
Debug, while unhealthy health checks remain Warning events. A recurring TODO
completion records the series, completed TODO, and newly created TODO identifiers
only after the transaction commits. Successful TODO create, update, status,
dependency, delete, and restore mutations also emit Information audit events
containing identifiers, versions, and operation-specific metadata.
Expected validation, not-found, domain, and concurrency responses are not logged
as exceptions; the global exception handler emits the single Error event for an
unexpected exception with its trace ID, method, and path, while its HTTP 500
completion is a Warning.

Logging excludes request bodies, TODO descriptions, cursor query values, and
MongoDB connection strings. Structured events use stable event IDs and named
properties rather than interpolated payloads. Code uses direct typed logger calls
such as `this.logger.LogInformation(eventId, template, values)` so the emitting
class and event shape remain explicit without provider-specific APIs.

## Startup responsibilities

Each layer exposes a dependency-injection extension. API startup composes those
extensions, while Infrastructure validates MongoDB settings, registers the
repository and health check, and initializes MongoDB indexes through a hosted
service. This keeps `Program.cs` limited to composition and application startup.

Constructors fail fast with `ArgumentNullException` for every required injected
dependency. This makes direct construction and registration mistakes fail at
the composition boundary instead of later during a request. The optional
`MongoTransactionContext` parameter on `MongoTodoRepository` is deliberate:
normal dependency injection supplies the scoped context, while direct repository
construction can fall back to a new context when no transaction is required.
