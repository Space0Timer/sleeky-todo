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

## Persisted React workflow

The React client treats the API as the source of truth rather than maintaining
a session-only TODO collection. Scope, filter, and sort changes replace the
current cursor chain; Load More is the only operation that appends a server
page. Full TODO details are fetched on demand for management, while list cards
use the smaller projection returned by the list endpoint.

Every update, status, dependency, delete, and restore mutation carries the last
observed version and refreshes the list after success. A stale-version response
is shown explicitly and requires a Reload Latest Version action, preserving the
same no-silent-overwrite rule as the API.

The dependency selector loads at most 100 active TODO projections ordered by
name and searches within that bounded set. This keeps browser memory and network
work bounded without adding a new API contract, at the cost of not exposing
candidates beyond that first page. Server-side dependency search is the chosen
follow-up if larger datasets must be supported.

## MongoDB

MongoDB is the persistence store because TODO documents naturally contain
embedded and evolving fields such as dependencies and recurrence metadata. The
application uses a single-member replica set locally so recurring completion
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

Deleting a TODO that is still required by an active, non-archived dependent is
rejected. Archived and deleted dependents do not prevent deletion.

## Dependency graph and blocked transitions

Each TODO stores outgoing dependency IDs. Adding an edge requires an active
target and rejects self-dependencies, duplicates, and direct or transitive
cycles. Cycle detection traverses the graph breadth-first and batch-loads each
frontier, avoiding one repository call per node and terminating safely even if
legacy data already contains a cycle.

Blocked state is evaluated from one batch read of the source TODO's dependency
IDs. A missing, deleted, archived, or non-completed dependency blocks the
source. Blocked TODOs cannot enter `InProgress` or `Completed`; other valid
status transitions remain available. Dependency and status commands use the
same optimistic version contract as the CRUD commands.

## API failure contract

The API uses one global exception handler rather than controller-level
try/catch blocks. Application not-found, concurrency, and domain-rule
exceptions map to stable RFC Problem Details responses. FluentValidation and
ASP.NET Core model-binding failures share the same 400 title, detail, trace ID,
and field-error shape.

## Provider-neutral structured logging

Serilog is the API host's logging provider, but application and infrastructure
code depend only on Microsoft `ILogger<T>`. Typed loggers provide automatic
source categories and allow per-category filtering without coupling use cases
or persistence code to Serilog. Static Serilog access is restricted to bootstrap,
fatal startup reporting, and shutdown flushing in `Program.cs`.

The logging pipeline produces a condensed HTTP completion event, trace context,
MediatR request timing, index-initialization events, and a post-commit event when
a recurring completion creates its next TODO. Successful TODO mutations emit
Information audit events through direct typed `ILogger<T>` calls with stable
event IDs and structured placeholders. Logging records identifiers, versions,
and operational metadata, not request bodies, descriptions, cursor values, or
connection strings. Successful health probes are Debug events, while unhealthy
probes remain Warning events. Known 400, 404, and 409 outcomes remain normal
request events. Unexpected exceptions are logged once at Error with the trace
ID, method, and path by the global exception handler; the corresponding HTTP
completion remains a Warning to avoid a second error event for the same failure.

## Fail-fast injected dependencies

Required constructor-injected services are guarded with
`ArgumentNullException.ThrowIfNull`. Although the runtime container normally
guarantees required registrations, explicit guards provide deterministic
failures for direct construction, tests, factories, and future registration
changes. Optional parameters are not converted into required dependencies; the
repository's optional transaction context remains an intentional fallback for
non-transactional direct construction.

## Deterministic TODO list pagination

The list read path uses a dedicated `ITodoListReader` abstraction rather than
expanding the mutation-oriented aggregate repository. Infrastructure implements
the reader as a MongoDB aggregation so dependency documents can be joined and
blocked state can be calculated before a blocked/unblocked filter, cursor, or
limit is applied.

Cursors are versioned JSON payloads encoded with Base64URL. They bind the last
sort value and TODO ID to the selected sort, direction, scope, and filter
signature. Reusing a cursor after any bound option changes is a 400 error. Every
sort uses the TODO ID in the same direction as its final tie-breaker, and the
reader fetches one item beyond the requested limit to decide whether to return
another cursor.

Priority ordering is Low, Medium, High; status ordering is NotStarted,
InProgress, Completed, Archived. These are explicit business orders and do not
depend on the alphabetical BSON representation.

## Local replica set

Local development uses one MongoDB 7.0 replica-set member. The Compose
initializer is idempotent: it checks replica-set status and initiates `rs0` only
for an uninitialized database. The member advertises `localhost:27017` so the
host-run API can use the committed connection string. This single-member setup
supports optimistic writes and transactional recurring completion.

## Recurrence and atomic completion

Recurrence is represented by a domain value object containing a schedule type,
positive interval, unit, and monthly anchor where relevant. Standard daily,
weekly, and monthly schedules use an interval of one; custom schedules support
every N days, weeks, or months. Calculations start from the scheduled due date,
not completion time, so late completion does not cause schedule drift. Monthly
calculation reconstructs the target day from the stored anchor, preserving
end-of-month and leap-year behavior.

The first recurring TODO receives a series ID and occurrence number 1. A real
transition into `Completed` raises `TodoCompletedDomainEvent`; a no-op
Completed-to-Completed request raises nothing. Application dispatches the event
in-process while the MongoDB session transaction is active. Its handler inserts
the next occurrence with copied name, description, priority, recurrence, and
series data, but no dependencies. A handler failure aborts the completed update.

The transaction reuses the existing expected-version filter. A unique partial
index on `seriesId + occurrenceNumber` is the second idempotency boundary.
Concurrent completion therefore produces one committed completion and next
occurrence, while the stale request returns 409.

## Authentication outside the current scope

The first vertical slice has no authentication or authorization. Adding a
partial identity model before user ownership and access requirements are known
would create misleading security boundaries. Until authentication is designed,
the API is intended for local development only and must not be exposed publicly.

When authentication is added, TODO ownership, authorization rules, API security
schemes, and test isolation by user must be designed together.

## Deferred decisions

Retention cleanup scheduling and production authentication remain deferred
until their corresponding vertical slices.
