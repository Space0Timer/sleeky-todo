# Sleeky To-Do

A layered-monolith TODO application: ASP.NET Core and MediatR over MongoDB,
with a React and TypeScript client. Every TODO belongs to a signed-in user,
and the whole slice — domain rules, persistence, HTTP API, and UI — is
implemented and tested end to end.

## Features

- **CRUD with optimistic concurrency.** Create, read, update, and delete with
  validation. Every write carries the version last read; a stale save is
  rejected with `409` and the client offers to reload rather than overwrite
  silently.
- **List queries.** Filters by status, priority, and due date; word-prefix
  search across name and description; `Active`, `Archived`, and `Deleted`
  scopes; sorting; deterministic cursor pagination.
- **Dependencies and blocking.** A TODO can depend on others. Direct and
  transitive cycles are rejected, and a TODO whose prerequisites are incomplete,
  deleted, or archived is blocked from moving to In progress or Completed.
- **Recurrence.** Daily, weekly, monthly, and custom day/week/month intervals,
  with monthly anchor preservation. Completing a recurring TODO creates the
  next occurrence in the same MongoDB transaction — never zero, never two.
- **Recoverable deletion.** Delete moves a TODO to Trash with a 90-day restore
  window; a TODO that an active dependent still needs cannot be deleted.
- **Bulk actions.** Status changes, restores, and deletes over a selection,
  all-or-nothing, mirroring the single-item routes.
- **Authentication and ownership.** OpenID Connect login, an encrypted
  HttpOnly cookie session, and per-user isolation — another user's TODO
  answers `404`, not `403`.
- **AI assistant.** Natural-language bulk actions over the same commands the
  toolbar uses, streamed as server-sent events, with confirmation before
  anything destructive. Bring your own key — Anthropic or any
  OpenAI-compatible endpoint — or use one configured for the deployment.

Deliberately not built, with reasons in
[docs/decision-log.md §3](docs/decision-log.md#3-what-i-chose-not-to-build-and-why):
user registration, real-time push, the physical purge job, provider-initiated
logout, and server-side assistant history.

## Prerequisites & quick start

- .NET SDK 10.0.302
- Node.js 24.19.0 with Corepack (Yarn 4.18.0)
- Docker with Docker Compose

Three terminals.

**1. Start MongoDB and Keycloak**, and wait for both to report healthy:

```sh
docker compose up -d
docker compose ps
```

**2. Run the API.** The first run on a machine may need
`dotnet dev-certs https --trust`.

```sh
dotnet run --project src/Sleeky.Todo.Api --launch-profile https
```

**3. Run the React client:**

```sh
cd src/sleeky-todo-web && corepack yarn install && corepack yarn dev
```

Open `http://localhost:5173` and sign in as `alice` / `alice-password`.

### Try it out

- **Per-user isolation.** Create a TODO as `alice`, then sign in as `bob` /
  `bob-password` in a private window. Bob's list is empty.
- **Dependencies.** Link two TODOs; the dependent cannot start until its
  prerequisite is completed, and a cycle is rejected.
- **Recurrence.** Create a TODO with Repeat enabled and complete it; the next
  occurrence appears.
- **Trash.** Delete a TODO, open the Trash tab, restore it.
- **Concurrency.** Open Manage on one TODO in two tabs, save in both; the
  second reports a stale version and offers Reload.
- **Assistant.** Configure a model (next section), then ask the Assistant
  panel to, say, complete everything due this week. Destructive actions come
  back as a proposal to confirm first.

### AI assistant with a local model

The assistant needs a chat model. The way to try it without an API key is
[Ollama](https://ollama.com) on the host: pull a model that supports tool
calling, then point the assistant at Ollama's OpenAI-compatible endpoint.
Development already allows loopback endpoints
(`Assistant:AllowPrivateEndpoints`), which the default configuration refuses.

```sh
ollama pull llama3.2
```

In the app, open **Settings** in the Assistant panel and enter:

| Field    | Value                       |
| -------- | --------------------------- |
| Provider | OpenAI-compatible           |
| Model    | `llama3.2`                  |
| Base URL | `http://localhost:11434/v1` |
| API key  | anything, e.g. `ollama`     |

**Test** probes the endpoint without saving; **Save** stores the configuration
for that user, with the key encrypted. The key field is required because a
hosted endpoint needs one; Ollama ignores it.

To give every user the same provider without touching the form, set it at the
application level instead:

```sh
Assistant__Provider=OpenAiCompatible Assistant__BaseUrl=http://localhost:11434/v1 Assistant__Model=llama3.2 Assistant__ApiKey=ollama dotnet run --project src/Sleeky.Todo.Api --launch-profile https
```

An Anthropic key works the same way — provider `Anthropic`, no base URL —
and the default model is `claude-sonnet-5`.

## MongoDB

`docker compose up -d` starts a single-node replica set (`rs0`), which the
recurring-completion transaction needs, and an idempotent initializer, so
repeated `up` is safe. The API reads the `MongoDb` configuration section; the
committed default is `mongodb://localhost:27017/?replicaSet=rs0`, database
`sleekyTodo`. Override any value through environment variables:

```sh
MongoDb__ConnectionString="mongodb://host:27017/?replicaSet=rs0" dotnet run --project src/Sleeky.Todo.Api
```

Indexes are created at startup. `docker compose down` keeps the data volume;
`docker compose down --volumes` discards it, which is also the fix for a local
database written by an older, incompatible schema.

## Authentication

Login is OpenID Connect; the application session is an encrypted HttpOnly
cookie, so the browser never holds an access, ID, or refresh token. Compose
starts a Keycloak realm at `http://localhost:8080` with two seeded users:

| Username | Password         |
| -------- | ---------------- |
| `alice`  | `alice-password` |
| `bob`    | `bob-password`   |

`appsettings.Development.json` already points at that realm. Elsewhere, set
the provider and client and keep the secret out of source control:

```sh
dotnet user-secrets set "Authentication:ClientSecret" "secret-value" --project src/Sleeky.Todo.Api
```

Unauthenticated API requests answer `401` rather than redirecting. Mutations
also need an antiforgery token in the `X-CSRF-TOKEN` header, issued by
`GET /api/auth/antiforgery`. Sign-out ends the provider's session too, so the
next sign-in prompts for credentials again.

## API

Swagger UI documents every route and is served in Development at
`https://localhost:7238/swagger` (OpenAPI document at
`/swagger/v1/swagger.json`). Failures use RFC Problem Details: `400`
validation, `404` not found, `409` stale version or domain-rule conflict.
`GET /health` reports whether MongoDB answers a ping.

## Tests

Domain and application suites need nothing running:

```sh
dotnet test
```

Repository and API integration tests start their own MongoDB container and are
opt-in; without the variable they are skipped and still report "Passed":

```sh
RUN_MONGODB_INTEGRATION_TESTS=true dotnet test tests/Sleeky.Todo.IntegrationTests
```

Browser tests drive the real Keycloak login, so `docker compose up -d` must be
running:

```sh
cd src/sleeky-todo-web && corepack yarn playwright install chromium && corepack yarn test:e2e
```

## Deployment

One image serves the client and the API from a single origin:

```sh
docker build --tag sleeky-todo .
```

Three settings decide whether a deployment works — registering the origin with
the identity provider, trusting forwarded headers, and persisting the
data-protection key ring. They are covered in
[docs/deployment.md](docs/deployment.md).

## Further reading

- [docs/architecture.md](docs/architecture.md) — component diagram, request
  flow, and each boundary the code holds: persistence, concurrency, the
  recurring-completion transaction, authentication, ownership, the assistant,
  logging
- [docs/decision-log.md](docs/decision-log.md) — two pages: how the ambiguous
  requirements were read, the key trade-offs, what was deliberately not built,
  and what more time would change
- [docs/decision-log-detailed.md](docs/decision-log-detailed.md) — every
  decision as it was made, with the alternatives that were rejected
- [docs/coding-standards.md](docs/coding-standards.md) — the conventions the
  code follows
