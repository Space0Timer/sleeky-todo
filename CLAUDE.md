# Sleeky TODO

ASP.NET Core on MongoDB with a React SPA. The backend is layered `Domain` →
`Application` → `Infrastructure` → `Api`; the SPA lives in `src/sleeky-todo-web`
and is not part of `Sleeky.Todo.sln`.

## Read before you change code

`docs/coding-standards.md` is binding. Read it before changing project layout,
type visibility, or test structure. Its rules are not all enforced by the build,
so **existing code is not a reliable guide to them** — infer conventions from the
document, not from what compiles.

`docs/architecture.md` and `docs/decision-log.md` record why the current
boundaries exist. Check the decision log before reopening a settled design.

## The rules that cost the most when missed

- **Never add `InternalsVisibleTo`.** Tests reach production code through
  interfaces, handlers, repository contracts, or HTTP endpoints. To exercise an
  internal implementation, register infrastructure the way the composition root
  does and resolve the contract from the container.
- **A folder under `Todos/Commands` or `Todos/Queries` is one operation or a
  group of related operations** — never a bag of helpers sitting beside the
  operations that use it. Shared parts live in the folder that contains those
  operations, as in `Commands/Bulk`.
- **Persistence documents, serializers, and driver details stay inside
  `Infrastructure`** and never appear in a public signature.
- **Repository writes throw on a lost concurrency race.** They do not return
  null, and callers do not re-translate a null into an exception.
- Test observable behaviour and persisted contracts, not private helpers.

## Verifying a change

A clean build is **not** proof of compliance: warnings are not yet treated as
errors, so StyleCop violations pass silently. Read the analyzer warnings your
change introduces and fix them before committing.

Backend, from the repository root:

- `dotnet build Sleeky.Todo.sln`
- `dotnet test` runs the unit suites; the integration suites report inconclusive
  without MongoDB.
- `RUN_MONGODB_INTEGRATION_TESTS=true dotnet test` runs everything. It needs
  Docker — Testcontainers starts MongoDB, as a replica set where a suite covers
  transactions.

Frontend, from `src/sleeky-todo-web`:

- `corepack yarn lint` (oxlint plus stylelint)
- `corepack yarn build`
- `corepack yarn test:e2e`

## Conventions

- Branch from `develop`, which is also the pull request target.
- Commit messages are Conventional Commits. The body explains why the change was
  needed, not what the diff shows.
