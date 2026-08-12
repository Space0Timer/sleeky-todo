# Decision Log

This document records architecture decisions, requirement interpretations,
trade-offs, known limitations, and intentionally omitted features.

## Layered monolith

The backend is a layered monolith split into Domain, Application,
Infrastructure, and API projects. This keeps business rules independent of HTTP
and MongoDB without introducing distributed-system overhead. Dependencies point
inward: API composes Application and Infrastructure, Infrastructure implements
Application abstractions, and Domain has no outward dependencies.

The React client is a separate build artifact but consumes the same HTTP API.
Additional deployable backend services should only be introduced when an
independent scaling or ownership requirement justifies them.

## CQRS with MediatR

HTTP controllers send commands and queries through MediatR. Each request has a
single handler, and cross-cutting validation and domain-exception translation
run as pipeline behaviors. Commands and queries share one MongoDB data model;
CQRS here separates use-case code rather than introducing separate read and
write databases.

This keeps controllers thin and handlers independently testable while avoiding
an event bus or messaging infrastructure for an in-process application.

## MongoDB

MongoDB is the persistence store because TODO documents naturally contain
embedded and evolving fields such as dependencies and recurrence metadata. The
application uses a single-member replica set locally so later recurrence work
can use MongoDB transactions without changing development topology.

MongoDB-specific documents, BSON serializers, indexes, and repository behavior
remain inside Infrastructure.

## Date-only due dates

Due dates are calendar dates rather than instants, so Domain and Application use
`DateOnly`. MongoDB stores each due date as an ISO `yyyy-MM-dd` string through an
explicit BSON serializer, and the API uses the matching JSON date format. This
avoids timezone conversion changing the day while retaining lexicographic date
ordering.

## MongoDB repository boundary

Application code depends on `ITodoRepository`, while Infrastructure provides `MongoTodoRepository`. The repository uses `IMongoDatabase` directly rather than introducing a thin `MongoDbContext` wrapper. MongoDB documents and mapping types remain internal so persistence-specific BSON concerns cannot leak into Application or Domain code.

Integration tests use the public repository contract. Exact storage representations are checked as raw BSON after writing through the repository, avoiding both `InternalsVisibleTo` and a public persistence document type.

## Optimistic concurrency

The numeric `version` field is the sole concurrency token; `updatedAt` is not used for concurrency. Clients send the version they last read with every mutable request.

MongoDB mutations atomically filter by TODO ID and expected version, write version `N + 1`, and return the persisted document. A missing match indicates a stale write, which the application represents as `ConcurrencyConflictException`. The API maps this known exception to HTTP 409 Problem Details.

This design avoids locks and prevents lost updates. Concurrent integration tests verify that exactly one mutation succeeds when two writers submit update, delete, or restore operations using the same version.

## Ninety-day recoverable deletion

“Data should not be permanently lost when deleted” is interpreted as requiring a recoverable soft-delete period. Delete records the UTC deletion time and a purge time exactly 90 days later; it does not remove the MongoDB document. Normal queries exclude deleted records.

Restore is allowed strictly before the purge timestamp and requires the latest version. At the purge boundary the record is expired and cannot be restored. A later retention job will physically remove expired records; that cleanup is intentionally separate from request handling.

Deleting a TODO that is still required by active dependents will be rejected once dependency rules are implemented.

## API failure contract

The API uses one global exception handler rather than controller-level
try/catch blocks. Application not-found, concurrency, and domain-rule
exceptions map to stable RFC Problem Details responses. FluentValidation and
ASP.NET Core model-binding failures share the same 400 title, detail, trace ID,
and field-error shape.

## Local replica set

Local development uses one MongoDB 7.0 replica-set member. The Compose
initializer is idempotent: it checks replica-set status and initiates `rs0` only
for an uninitialized database. The member advertises `localhost:27017` so the
host-run API can use the committed connection string. This single-member setup
is sufficient to exercise optimistic writes now and transactions in the later
recurrence slice.

## Authentication outside the current scope

The first vertical slice has no authentication or authorization. Adding a
partial identity model before user ownership and access requirements are known
would create misleading security boundaries. Until authentication is designed,
the API is intended for local development only and must not be exposed publicly.

When authentication is added, TODO ownership, authorization rules, API security
schemes, and test isolation by user must be designed together.

## Deferred decisions

Cursor pagination, recurring occurrences, dependency graph evaluation,
retention cleanup scheduling, and production authentication remain deferred
until their corresponding vertical slices.
