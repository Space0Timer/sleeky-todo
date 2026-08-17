# Sleeky To-Do Web

React and TypeScript frontend for Sleeky To-Do.

Every list belongs to a space, and the open space lives in the URL
(`/spaces/{spaceId}`). `/` resolves one — the space last visited, otherwise the
personal space the server ensures — and redirects. Switching spaces remounts
the page, so filters, paging, selection, and the assistant's conversation all
start again in the space now on screen. A member with read access sees the list
without the controls that would change it.

The application uses the persisted list API for Active, Archived, and Trash
views. It supports cursor-based loading, status/priority/due-date/dependency
filters, deterministic sorting, create and edit flows, status transitions,
dependency management, recurring schedules, soft deletion, and restore.

Blocked TODOs identify their incomplete prerequisites and prevent invalid
status transitions. API validation, domain-rule failures, and optimistic-
concurrency conflicts are rendered from Problem Details; stale records can be
reloaded directly from the conflict banner.

## Development

Use Node.js 24.19.0.

```sh
corepack yarn install
corepack yarn dev
```

Vite serves the client at `http://localhost:5173` and proxies API calls to the
backend HTTPS profile at `https://localhost:7238`.

## Checks

With the repository's MongoDB Compose services running:

```sh
corepack yarn lint
corepack yarn build
corepack yarn playwright install chromium
corepack yarn test:e2e
```

Playwright starts dedicated API and Vite processes, uses the
`sleekyTodoPlaywright` database, and drops only that database after the run.
