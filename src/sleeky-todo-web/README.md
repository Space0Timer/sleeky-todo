# Sleeky To-Do Web

React and TypeScript frontend for Sleeky To-Do.

The first application shell includes a typed API client and create, edit,
soft-delete, and restore flows. It renders FluentValidation messages and
optimistic-concurrency conflicts returned as Problem Details.

The backend does not expose a list endpoint yet, so the shell displays TODOs
created during the current browser session. Server-backed listing and browser
session recovery belong to the later pagination slice.

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
