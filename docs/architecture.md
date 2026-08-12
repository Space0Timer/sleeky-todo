# Architecture

The application will use a layered monolith:

```text
React
  -> ASP.NET Core API
  -> Application commands and queries
  -> Domain and infrastructure
  -> MongoDB
```

Detailed component responsibilities will be recorded as each vertical slice is implemented.

The Application layer owns persistence and time abstractions. Infrastructure supplies their runtime implementations, keeping command handlers testable and independent of MongoDB and the system clock.

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

## Startup responsibilities

Each layer exposes a dependency-injection extension. API startup composes those
extensions, while Infrastructure validates MongoDB settings, registers the
repository and health check, and initializes MongoDB indexes through a hosted
service. This keeps `Program.cs` limited to composition and application startup.
