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

Dedicated simultaneous-write concurrency tests remain deferred to the optimistic-concurrency step.

## Build the scaffold

```sh
dotnet restore
dotnet build
dotnet test
cd src/sleeky-todo-web
corepack yarn install
corepack yarn build
```
