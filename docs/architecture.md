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

## Optimistic concurrency

Every mutable TODO carries a numeric version. Update, soft-delete, and restore requests include the version last read by the client.

The repository performs each mutation as one MongoDB `FindOneAndReplace` operation with a filter equivalent to:

```text
_id == todoId AND version == expectedVersion
```

Update and soft-delete also require an active document. Restore requires a deleted document. The replacement increments the version by one, and `ReturnDocument.After` returns the actual persisted state.

If the filter matches nothing, the repository returns `null`; command handlers map that result to `TodoConcurrencyException`. This covers the race between the handler's initial read and its write. `UpdatedAt` remains ordinary data and is never used as the concurrency token.

Integration tests issue simultaneous mutations with the same version and verify that exactly one succeeds for update/update, update/delete, and restore/restore races.

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

The background job that physically removes records after `purgeAt` is separate from this recoverable lifecycle. Dependency-aware deletion checks will be added with dependency rules.
