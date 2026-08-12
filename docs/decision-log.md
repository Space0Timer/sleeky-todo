# Decision Log

This document will record the final architecture decisions, requirement interpretations, trade-offs, known limitations, and intentionally omitted features.

## Decisions to document

- Layered monolith
- MongoDB and local replica-set development
- CQRS with MediatR
- Date-only due dates
- Optimistic concurrency
- Cursor pagination
- Recurring occurrences as separate records
- Ninety-day recoverable deletion

## MongoDB repository boundary

Application code depends on `ITodoRepository`, while Infrastructure provides `MongoTodoRepository`. The repository uses `IMongoDatabase` directly rather than introducing a thin `MongoDbContext` wrapper. MongoDB documents and mapping types remain internal so persistence-specific BSON concerns cannot leak into Application or Domain code.

Integration tests use the public repository contract. Exact storage representations are checked as raw BSON after writing through the repository, avoiding both `InternalsVisibleTo` and a public persistence document type.

Dedicated optimistic-concurrency race tests remain intentionally deferred to the concurrency phase.
