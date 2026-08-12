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

The repository scaffold is complete. Application behavior and infrastructure configuration have not been implemented yet.

## Prerequisites

- .NET SDK 10.0.302
- Node.js 24.19.0

## Build the scaffold

```sh
dotnet restore
dotnet build
dotnet test
cd src/sleeky-todo-web
corepack yarn install
corepack yarn build
```
