# Sleeky To-Do

A layered-monolith TODO application built with ASP.NET Core, MongoDB, MediatR,
React, and TypeScript. The current vertical slice supports creating, retrieving,
updating, soft-deleting, and restoring TODOs with validation and optimistic
concurrency. The list API supports filters, scopes, deterministic cursor
pagination, and blocked-state projection. Dependency mutations enforce graph
integrity, blocked status transitions, and dependency-aware deletion. Recurring
completion atomically creates the next scheduled occurrence. Every TODO belongs
to a signed-in user: login uses OpenID Connect, and the browser session is an
encrypted HttpOnly cookie rather than a token held by the client.

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

Authentication and per-user ownership are implemented end to end: the API
requires an authenticated session, every TODO query is scoped to its owner, and
the React client has a login page, protected route, and sign-out control.

## Prerequisites

- .NET SDK 10.0.302
- Node.js 24.19.0
- Corepack with Yarn 4.18.0
- Docker with Docker Compose (daemon API 1.47 or newer, for browser and
  integration tests)

## Quick start

Three terminals. The sections further down explain each piece; this is the
shortest path to a running, signed-in application.

**1. Start MongoDB and Keycloak.**

```sh
docker compose up -d
```

Both must report healthy before the API will start cleanly:

```sh
docker compose ps
```

**2. Run the API.** The first run on a machine may need
`dotnet dev-certs https --trust`.

```sh
dotnet run --project src/Sleeky.Todo.Api --launch-profile https
```

**3. Run the React client, in a second terminal.**

```sh
cd src/sleeky-todo-web && corepack yarn install && corepack yarn dev
```

Open `http://localhost:5173`. The application redirects to `/login`; sign in as
`alice` / `alice-password`.

### Try it out

- **Per-user isolation.** Create a TODO as `alice`, then open a private window
  and sign in as `bob` / `bob-password`. Bob's list is empty. Requesting one of
  Alice's TODOs by ID returns `404`, not `403`, so the response does not reveal
  that the identifier exists.
- **Dependencies and blocking.** Add a dependency between two TODOs. The
  dependent cannot move to In progress or Completed until its prerequisite is
  completed, and a cycle is rejected.
- **Recurrence.** Create a TODO with Repeat enabled and complete it. The next
  occurrence is created in the same transaction and its ID is reported.
- **Recoverable deletion.** Delete a TODO, open the Trash tab, and restore it.
- **Concurrency.** Open Manage on a TODO in two tabs, save in one, then save in
  the other. The second reports a stale version and offers Reload latest
  version rather than overwriting silently.
- **Session handling.** Sign out and confirm the list is unreachable. Because
  sign-out clears the application cookie only, the provider session can outlive
  it and the next sign-in may not prompt for credentials.

### Run the tests

Domain and application suites need nothing running:

```sh
dotnet test
```

Repository and API integration tests start their own MongoDB container, so they
need Docker and are opt-in. Without the variable they are skipped, and a skipped
suite reports the same "Passed" summary as one that ran:

```sh
RUN_MONGODB_INTEGRATION_TESTS=true dotnet test tests/Sleeky.Todo.IntegrationTests
```

Browser tests drive the real Keycloak login form, so `docker compose up -d` must
be running first. They start their own API and Vite server against a dedicated
`sleekyTodoPlaywright` database and drop it afterwards:

```sh
cd src/sleeky-todo-web && corepack yarn playwright install chromium && corepack yarn test:e2e
```

## Local MongoDB replica set

Start MongoDB, run the idempotent replica-set initializer, and start the local
identity provider:

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

## Authentication

Login uses OpenID Connect; the application session is an encrypted HttpOnly
cookie. The React client never receives an access, ID, or refresh token.

`docker compose up -d` starts a Keycloak realm at `http://localhost:8080` with
two seeded users for local development and browser tests:

| Username | Password         | Display name   |
| -------- | ---------------- | -------------- |
| `alice`  | `alice-password` | Alice Anderson |
| `bob`    | `bob-password`   | Bob Baxter     |

The development settings in `appsettings.Development.json` already point at that
realm. For any other environment, set the provider and client, and keep the
secret out of source control:

```sh
dotnet user-secrets set "Authentication:ClientSecret" "secret-value" --project src/Sleeky.Todo.Api
```

Unauthenticated API requests return `401` rather than a redirect, so the client
can react to them. Mutations additionally require an antiforgery token supplied
in the `X-CSRF-TOKEN` header; `GET /api/auth/antiforgery` issues one, and the
client refreshes it whenever the authentication state changes. Sign-out clears
the application cookie only, so the identity provider session can outlive it and
a following sign-in may not prompt for credentials again.

Every TODO carries an owner. The repository and list reader apply the owner
filter themselves, so a request for another user's TODO returns `404` rather
than disclosing that the identifier exists. TODO documents created before this
change have no owner: recreate disposable local data with
`docker compose down --volumes` followed by `docker compose up -d`.

Production deployments must persist Data Protection keys so cookie sessions
survive restarts and stay valid across API instances.

## HTTP API

The API routes are:

- `GET /api/auth/login`
- `GET /api/auth/me`
- `GET /api/auth/antiforgery`
- `POST /api/auth/logout`
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

Open `http://localhost:5173` and sign in as one of the seeded users. Vite
proxies `/api`, `/health`, and the OpenID Connect callback paths to the local
HTTPS API, so no browser-specific CORS configuration is required. The proxy
deliberately preserves the browser's origin: with the API's own host instead,
the provider would return the browser to an origin where the login correlation
cookie does not exist.

## Tests

Run the fast domain and application suites:

```sh
dotnet test
```

Run the MongoDB repository and complete API integration suite with Docker:

```sh
RUN_MONGODB_INTEGRATION_TESTS=true dotnet test tests/Sleeky.Todo.IntegrationTests
```

Browser tests drive the real login flow, so Keycloak and MongoDB must be running
first (`docker compose up -d`).

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

## Container image

The image builds the client and the API together and serves both from one
origin, so the session cookie has a single origin to belong to and no gateway
is needed to put the two behind one host.

```sh
docker build --tag sleeky-todo .
```

Development is unaffected: the API carries no `wwwroot` when it runs from
source, so requests for the client are answered 404 and the Vite server keeps
serving it over the proxy as before.

Three settings decide whether a deployment works, and none of them show up
locally.

**The origin must be registered with the identity provider.** The realm in
`docker/keycloak` lists only the development origins. A deployment's own
`https://host/signin-oidc` has to be added to the client's redirect URIs, and
the origin to its web origins, or sign-in fails at the callback.

**Forwarded headers decide the redirect URI.** The container listens on plain
HTTP and expects TLS to be terminated ahead of it, so the scheme and host it
builds the OpenID Connect redirect URI from come from `X-Forwarded-Proto` and
`X-Forwarded-Host`. Only loopback is believed by default; name the proxy, or
the network it sits on, before trusting it:

```json
{
  "ForwardedHeaders": {
    "KnownProxies": ["10.1.2.3"],
    "KnownNetworks": ["10.1.0.0/16"]
  }
}
```

**Data protection keys must outlive the container.** The key ring encrypts two
things: session cookies, and every user's stored provider API key. Losing it
signs everyone out, stops two replicas reading each other's cookies, and leaves
saved provider keys unreadable — the API is write-only, so a user cannot even
look up what they had and must enter it again.

The image writes the ring to `/keys`, owned by the user it runs as, so
persisting it is a mount rather than a mount plus a setting:

```sh
docker run --volume sleeky-todo-keys:/keys ... sleeky-todo
```

Set `DataProtection:KeyRingPath` only to move it somewhere else, such as a
location every replica shares:

```json
{
  "DataProtection": { "KeyRingPath": "/keys" }
}
```

Without a volume the keys still live on the container's writable layer and
still vanish with it. Two rules make the mount worth having:

- **Back the ring up with the database, and restore them together.** Restoring
  MongoDB against a different ring gives you a database of provider keys nobody
  can decrypt. They are one backup unit.
- **Never prune the directory.** Keys roll roughly every 90 days and the old
  ones stay behind to read what they encrypted. A key saved eight months ago is
  readable only by the key that protected it.

A bind mount is the exception to the ownership note above: its permissions come
from the host, so the directory has to be writable by the container's user
(`APP_UID`, `1654` on the .NET base images) before it is mounted.
