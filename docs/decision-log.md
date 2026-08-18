# Decision Log

About two pages, in four parts. Mechanisms are described in
[architecture.md](architecture.md); each decision and the alternatives it
rejected are in [decision-log-detailed.md](decision-log-detailed.md).

## 1. How ambiguous requirements were interpreted

- **"Multiple users accessing the same TODO list concurrently."** Two
  concerns, both built. *The same list* is a **Space**: the shared list and
  authorization boundary, whose membership decides who may read and who may
  write. *Concurrently* is per-TODO optimistic concurrency, not a Space-wide
  lock, so two users editing different TODOs never conflict.
- **"Data should not be permanently lost when deleted."** Logical deletion into
  Trash (`deletedAt`, plus a `purgeAt` ninety days later), restore under the
  usual version check, and eventual physical purge as separate maintenance work.
- **"10,000+ TODOs."** Never load or count the collection in the browser or on
  the request path: server-side filtering and sorting, Space-leading indexes,
  projections, and deterministic keyset (cursor) pagination.
- **Recurrence.** Completing a recurring TODO creates its next occurrence
  automatically — once, so a reopened occurrence completed again does not
  create a second; completion and successor creation are atomic.
- **Dependencies.** A TODO may depend on several others and cannot move to In
  Progress (or Completed) until every dependency is Completed; missing,
  deleted, or archived dependencies count as incomplete, and cycles are
  rejected. Blocked state is derived from dependency state, never persisted.
- **Statuses.** A TODO nobody has started yet is `Open`, not a separate
  not-started state; `Archived` is frozen until unarchived.

## 2. Key architectural decisions and trade-offs

- **Layered modular monolith.** API → Application → Domain; Infrastructure
  plugs into the Application/Domain abstractions. Not microservices: nothing
  needs independent scaling, deployment, or ownership boundaries, so one
  deployable stays simple to run while its internal boundaries still allow
  extraction.
- **CQRS through MediatR.** Commands carry mutations and domain behaviour,
  queries read projections, and cross-cutting rules (validation, Space access,
  exception translation) are pipeline behaviours. CQRS here separates
  responsibilities and read/write shapes; it does not imply separate databases
  or services — one MongoDB model deliberately serves both.
- **MongoDB.** A TODO with embedded dependency IDs and recurrence metadata is a
  natural document; projections and compound indexes shape each query; a
  conditional `findOneAndReplace` gives atomic version-matched writes. Accepted:
  transactions need a replica set even locally, blocked state needs a `$lookup`
  rather than a join, and each query pattern needs its own index design.
- **Two aggregates, two concurrency boundaries.**
  `Space { Id, Name, Access[], Version }` with entries
  `{ SubjectId, SubjectType, Permission }`, `Read < Write < Owner`, owns
  membership; `Space.Version` covers only its name and access list.
  `Todo { SpaceId, CreatedByUserId, …, Version }` is a separate document —
  an embedded `Todos[]` would grow the Space towards MongoDB's size limit, make
  indexing and paging harder, and put unrelated writes in contention on one
  document. `CreatedByUserId` is audit information; authorization comes from
  membership. A TODO write never increments `Space.Version`, so members editing
  different TODOs get no false conflicts.
- **Optimistic concurrency.** The client reads `{ Id, Version }` and sends that
  version with each mutation; the repository issues one atomic conditional
  replace — `_id == id AND version == expected`, within the bound Space — that
  increments the version. No match means another writer got there first: HTTP
  `409 Conflict`, and the client offers to reload rather than overwrite. Write
  safety, not real-time synchronization.
- **Space authorization pipeline.** An `ISpaceScopedRequest` declares its Space
  and required level; `SpaceAccessBehavior` authorizes before the handler runs
  and binds the Space to `ISpaceScope`, which the repository and list reader
  read instead of a caller-supplied argument. No handler can forget the Space
  filter, and an unbound scope throws rather than reading every Space. Accepted
  race: a member removed after the check but before the write reaches MongoDB
  still lands it; closing that would cost every writer a Space-wide lock for a
  case the next request corrects.
- **404 versus 403.** A non-member gets `404` for the Space and everything
  under it, so the response never confirms the identifier exists. A member
  below the level a route needs gets `403`: they legitimately know the Space
  exists.
- **Derived dependency blocking.** `IsBlocked` is computed at read time, not
  persisted: a stored flag would need propagation whenever a prerequisite
  changes and risks going stale. The cost is an aggregation stage per page.
- **Recurrence transaction.** Marking the current occurrence Completed and
  inserting the next one happen in one MongoDB transaction, so neither a
  completed occurrence without a successor nor a successor beside an incomplete
  one can exist; a unique series index is the second wall. This is why Compose
  runs MongoDB as a single-node replica set.
- **Keyset pagination.** Pages are ordered by the requested sort field with
  `_id` as the tie-breaker, and the opaque cursor resumes from that pair: no
  high-offset scans, stable forward paging at any size. Not a snapshot — a
  TODO whose sort field changes between requests may move between pages.
- **Search.** Word-prefix search over a stored `searchTokens` array,
  index-backed and combined with the existing filters and cursor; no second
  service, no relevance ranking, no substring matching.
- **AI assistant.** Its tools send the same MediatR commands the toolbar does
  and never touch repositories, so it inherits Space authorization and every
  business rule; the turn's Space is authorized before the model is called,
  and destructive actions wait for confirmation.

## 3. What was deliberately not built

Left out on purpose, and why — the omissions are as much a decision as the
implementations.

- **Real-time collaboration.** No SignalR, WebSocket, or SSE change channel.
  Optimistic concurrency guarantees write safety; another user's change appears
  on the next load rather than being pushed. Not needed for concurrency, and a
  large expansion of scope.
- **Invitations and groups.** An access entry already carries a subject type,
  so a group is a second type plus a lookup; invitation workflows and groups
  were excluded.
- **Space lifecycle.** No delete Space, leave Space, or move TODO between
  Spaces: sharing and revoking membership covers the collaboration this
  application is for.
- **Scheduled physical purge.** Trash and retention metadata exist; a
  production-grade purge worker does not.
- **Microservices.** No demonstrated need for independent scaling, deployment,
  or ownership boundaries.
- Also excluded (reasons in the detailed log): user registration, which is the
  OIDC provider's feature; provider-initiated logout; server-side assistant
  history.

## 4. What I would add with more time

- **Real-time updates.** MongoDB change streams → server-side event stream
  (SSE) → clients subscribed to the current Space.
- **Retention purge worker.** Scheduled cleanup after the Trash retention
  period.
- **Observability.** OpenTelemetry traces and metrics.
- **Production schema/index deployment.** Move index management from
  application startup into an explicit migration step.

For deeper implementation reasoning and rejected alternatives, see
`docs/decision-log-detailed.md`.
