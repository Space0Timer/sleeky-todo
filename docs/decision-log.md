# Decision Log

This document will record the final architecture decisions, requirement interpretations, trade-offs, known limitations, and intentionally omitted features.

## Decisions to document

- Layered monolith
- MongoDB and local replica-set development
- CQRS with MediatR
- Date-only due dates
- Optimistic concurrency using a numeric version
- Cursor pagination
- Recurring occurrences as separate records
- Ninety-day recoverable deletion

## MongoDB repository boundary

Application code depends on `ITodoRepository`, while Infrastructure provides `MongoTodoRepository`. The repository uses `IMongoDatabase` directly rather than introducing a thin `MongoDbContext` wrapper. MongoDB documents and mapping types remain internal so persistence-specific BSON concerns cannot leak into Application or Domain code.

Integration tests use the public repository contract. Exact storage representations are checked as raw BSON after writing through the repository, avoiding both `InternalsVisibleTo` and a public persistence document type.

## Optimistic concurrency

The numeric `version` field is the sole concurrency token; `updatedAt` is not used for concurrency. Clients send the version they last read with every mutable request.

MongoDB mutations atomically filter by TODO ID and expected version, write version `N + 1`, and return the persisted document. A missing match indicates a stale write, which the application represents as `TodoConcurrencyException`. The API will map this known exception to HTTP 409 when ProblemDetails middleware is added.

This design avoids locks and prevents lost updates. Concurrent integration tests verify that exactly one mutation succeeds when two writers submit update, delete, or restore operations using the same version.

## Ninety-day recoverable deletion

“Data should not be permanently lost when deleted” is interpreted as requiring a recoverable soft-delete period. Delete records the UTC deletion time and a purge time exactly 90 days later; it does not remove the MongoDB document. Normal queries exclude deleted records.

Restore is allowed strictly before the purge timestamp and requires the latest version. At the purge boundary the record is expired and cannot be restored. A later retention job will physically remove expired records; that cleanup is intentionally separate from request handling.

Deleting a TODO that is still required by active dependents will be rejected once dependency rules are implemented.
