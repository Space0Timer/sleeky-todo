# Sleeky To-Do

A layered-monolith TODO application built with ASP.NET Core, MongoDB, MediatR, and React.

## Repository layout

- `src/Sleeky.Todo.Domain` — domain entities, value objects, rules, and interfaces
- `src/Sleeky.Todo.Application` — commands, queries, handlers, validation, and DTOs
- `src/Sleeky.Todo.Infrastructure` — MongoDB persistence and infrastructure services
- `src/Sleeky.Todo.Api` — HTTP API contracts, controllers, and middleware
- `src/sleeky-todo-web` — React and TypeScript web application
- `tests` — domain, application, and integration test projects
- `docs` — architecture notes and decision log

## Current status

The initial domain model, CRUD application handlers, request validation pipeline, MongoDB configuration, and MongoDB repository are implemented. API endpoints are still in progress.

## Prerequisites

- .NET SDK 10.0.302
- Node.js 24.19.0
- MongoDB replica set available on `localhost:27017`

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

The live repository integration tests require Docker and are opt-in:

```sh
RUN_MONGODB_INTEGRATION_TESTS=true dotnet test tests/Sleeky.Todo.IntegrationTests
```

Update, delete, and restore operations use the version last read by the client. MongoDB mutations atomically match both `_id` and `version`, increment the version, and return the persisted document. If another writer has already changed the record, the mutation returns no document and the application raises a concurrency conflict.

## Recoverable deletion

Deleting a TODO is recoverable rather than physical. The operation sets `deletedAt`, sets `purgeAt` to exactly 90 days later, updates `updatedAt`, and increments the version. Normal repository reads and existence checks exclude deleted records.

A deleted TODO can be restored before `purgeAt` by supplying its latest version. Restore clears both retention timestamps and increments the version again. Restore is rejected for active TODOs and when the 90-day boundary has been reached.

Permanent cleanup after `purgeAt` and deletion checks for active dependents remain later steps.

## Build the scaffold

```sh
dotnet restore
dotnet build
dotnet test
cd src/sleeky-todo-web
corepack yarn install
corepack yarn build
```
