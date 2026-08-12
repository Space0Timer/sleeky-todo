# Sleeky To-Do

A layered-monolith TODO application built with ASP.NET Core, MongoDB, MediatR,
React, and TypeScript. The current vertical slice supports creating, retrieving,
updating, soft-deleting, and restoring TODOs with validation and optimistic
concurrency. The list API supports filters, scopes, deterministic cursor
pagination, and blocked-state projection. Dependency mutations enforce graph
integrity, blocked status transitions, and dependency-aware deletion. Recurring
completion atomically creates the next scheduled occurrence.

## Architecture

Requests flow through a small set of explicit layer boundaries:

```text
React client
  -> ASP.NET Core controllers
  -> MediatR commands and queries
  -> domain model and repository abstraction
  -> MongoDB repository
```

Domain contains business state and rules. Application owns use cases and public
abstractions. Infrastructure implements MongoDB and clock services. API owns
HTTP contracts and error mapping. See [docs/architecture.md](docs/architecture.md)
and [docs/decision-log.md](docs/decision-log.md) for the detailed boundaries
and trade-offs.

## Repository layout

- `src/Sleeky.Todo.Domain` — domain entities, value objects, rules, and interfaces
- `src/Sleeky.Todo.Application` — commands, queries, handlers, validation, and DTOs
- `src/Sleeky.Todo.Infrastructure` — MongoDB persistence and infrastructure services
- `src/Sleeky.Todo.Api` — HTTP API contracts, controllers, and middleware
- `src/sleeky-todo-web` — React and TypeScript web application
- `tests` — domain, application, and integration test projects
- `docs` — architecture notes and decision log

## Current status

The CRUD, list-query, dependency-rule, and recurrence slices are implemented
through the domain, application, MongoDB, and HTTP API layers. Recurrence
supports daily, weekly, monthly, and custom day/week/month intervals with
monthly anchor preservation. The React client exposes the persisted scoped
list, filters, cursor loading, dependency management, recurring creation,
status workflows, recoverable deletion, and explicit stale-version recovery.

## Prerequisites

- .NET SDK 10.0.302
- Node.js 24.19.0
- Corepack with Yarn 4.18.0
- Docker with Docker Compose

## Local MongoDB replica set

Start MongoDB and run the idempotent replica-set initializer:

```sh
docker compose up -d
```

Confirm that both the database and one-time initializer are healthy/completed:

```sh
docker compose ps
docker compose exec mongodb mongosh --quiet --eval "rs.status().ok"
```

The status command should print `1`. The initializer only calls `rs.initiate`
when the replica set has not been configured, so repeated `docker compose up`
commands are safe.

Stop the services without deleting local data:

```sh
docker compose down
```

To intentionally remove the dedicated MongoDB volume as well, run
`docker compose down --volumes`.

## MongoDB configuration

The API reads MongoDB settings from the `MongoDb` configuration section. The committed local defaults use the `sleekyTodo` database and `todoItems` collection:

```json
{
  "MongoDb": {
    "ConnectionString": "mongodb://localhost:27017/?replicaSet=rs0",
    "DatabaseName": "sleekyTodo",
    "TodoItemsCollectionName": "todoItems"
  }
}
```

Override individual values through environment variables when needed:

```sh
MongoDb__ConnectionString="mongodb://localhost:27017/?replicaSet=rs0" dotnet run --project src/Sleeky.Todo.Api
```

Application handlers depend on `ITodoRepository`. Infrastructure implements that contract with `MongoTodoRepository`, which accesses the configured collection directly through `IMongoDatabase`. The BSON document and mapping types remain internal to Infrastructure.

Backend-owned identifiers are .NET `Guid` values stored as standard BSON UUIDs
(binary subtype 4). Their JSON representation remains the canonical UUID
string. Collections created by an older version that stored identifier fields
as BSON strings are not query-compatible with this representation. Migrate and
reinsert those documents, or recreate disposable local data with
`docker compose down --volumes` followed by `docker compose up -d`.

At startup, Infrastructure initializes compound active-record indexes for due
date, priority, status, and normalized-name sorting, plus the retained
soft-delete `purgeAt` index and an active-dependency lookup index. `GET /health`
verifies that MongoDB can answer a ping. A unique partial series/occurrence index
prevents duplicate recurring occurrences.

The live repository and API integration tests require Docker and are opt-in.
Each API test uses a dedicated database and drops only that database during
cleanup:

```sh
RUN_MONGODB_INTEGRATION_TESTS=true dotnet test tests/Sleeky.Todo.IntegrationTests
```

Update, delete, and restore operations use the version last read by the client. MongoDB mutations atomically match both `_id` and `version`, increment the version, and return the persisted document. If another writer has already changed the record, the mutation returns no document and the application raises a concurrency conflict.

## HTTP API

The API routes are:

- `GET /api/todos`
- `POST /api/todos`
- `GET /api/todos/{id}`
- `PUT /api/todos/{id}`
- `DELETE /api/todos/{id}`
- `POST /api/todos/{id}/restore`
- `POST /api/todos/{id}/dependencies`
- `DELETE /api/todos/{id}/dependencies/{dependencyId}`
- `PUT /api/todos/{id}/status`

Swagger UI is available at `/swagger`. Known failures use RFC Problem Details:
validation returns `400`, missing TODOs return `404`, and stale versions or
domain-rule conflicts return `409`. Every problem response includes a
`traceId`; validation responses also contain a stable `errors` object keyed by
camel-cased request field.

`GET /api/todos` accepts `status`, `priority`, `due-from`, `due-to`,
`dependencyStatus`, `scope`, `sortField`, `sortDirection`, `limit`, and
`cursor`. Scopes are `Active`, `Archived`, and `Deleted`; supported sort fields
are `DueDate`, `Priority`, `Status`, and `Name`. The default page size is 50 and
the maximum is 100. Each response contains `items` and a `nextCursor`; changing
filters, scope, or sorting requires starting again without the old cursor.

Dependency and status mutations require the TODO version last read by the
client. A dependency target must be active, distinct from the source, and not
already linked. Direct and transitive cycles are rejected. A TODO is blocked
when any dependency is missing, deleted, archived, or incomplete; blocked TODOs
cannot move to `InProgress` or `Completed`. A TODO required by an active,
non-archived dependent cannot be deleted.

`POST /api/todos` accepts an optional `recurrence` object with `type`,
`interval`, and, for custom schedules, `unit`. Completing a recurring TODO
through the status endpoint returns `nextOccurrenceId`. The completed update
and next-occurrence insert commit in one MongoDB transaction; the next TODO
copies the schedule and editable details but starts without dependencies.

## Run the API

Start the local replica set first, then run the HTTPS launch profile:

```sh
docker compose up -d
dotnet run --project src/Sleeky.Todo.Api --launch-profile https
```

Useful local URLs:

- API: `https://localhost:7238`
- Swagger UI: `https://localhost:7238/swagger`
- OpenAPI document: `https://localhost:7238/swagger/v1/swagger.json`
- Health check: `https://localhost:7238/health`

The development certificate may need to be trusted once with
`dotnet dev-certs https --trust`.

## Run the React client

In a second terminal:

```sh
cd src/sleeky-todo-web
corepack yarn install
corepack yarn dev
```

Open `http://localhost:5173`. Vite proxies `/api` and `/health` requests to the
local HTTPS API, so no browser-specific CORS configuration is required.

## Tests

Run the fast domain and application suites:

```sh
dotnet test
```

Run the MongoDB repository and complete API integration suite with Docker:

```sh
RUN_MONGODB_INTEGRATION_TESTS=true dotnet test tests/Sleeky.Todo.IntegrationTests
```

Run frontend checks and browser tests:

```sh
cd src/sleeky-todo-web
corepack yarn lint
corepack yarn build
corepack yarn playwright install chromium
corepack yarn test:e2e
```

## Recoverable deletion

Deleting a TODO is recoverable rather than physical. The operation sets `deletedAt`, sets `purgeAt` to exactly 90 days later, updates `updatedAt`, and increments the version. Normal repository reads and existence checks exclude deleted records.

A deleted TODO can be restored before `purgeAt` by supplying its latest version. Restore clears both retention timestamps and increments the version again. Restore is rejected for active TODOs and when the 90-day boundary has been reached.

Permanent cleanup after `purgeAt` remains a later step. Deletion is rejected
while an active, non-archived TODO depends on the target.

## Recurring completion

Recurring schedules calculate from the scheduled due date, not the day on which
the TODO is completed. Monthly schedules retain their original anchor: a
January 31 schedule advances to February's final day and then returns to March
31. Leap days are used whenever the target year permits them.

Each series has a stable ID and a one-based occurrence number. Entering
`Completed` raises an in-process completion event inside a MongoDB transaction.
The current occurrence update and next occurrence insert either both commit or
both roll back. Optimistic version matching and the unique
`seriesId + occurrenceNumber` index ensure concurrent requests create exactly
one next occurrence.

## Logging

The API routes Microsoft `ILogger<T>` events through Serilog. Application code
uses typed loggers so every event has a stable source category without taking a
dependency on Serilog itself. Console logs include structured request method,
path, status, duration, and trace context; request bodies, TODO descriptions,
cursors, and MongoDB connection strings are not logged.

Logging levels and sinks are configured in `src/Sleeky.Todo.Api/appsettings.json`.
The development override enables debug events for Sleeky To-Do categories.
Health-check request completion events are kept at debug level to reduce noise.

## Build everything

```sh
dotnet restore
dotnet build
dotnet test
cd src/sleeky-todo-web
corepack yarn install
corepack yarn build
```
