# Decision Log

Two pages, in four parts. Mechanisms are described in
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
  automatically; completion and successor creation are atomic.
- **Dependencies.** A TODO may depend on several others and cannot move to In
  Progress (or Completed) until every dependency is Completed; missing,
  deleted, or archived dependencies count as incomplete, and cycles are
  rejected. Blocked state is derived from dependency state, never persisted.
- **Statuses.** A TODO nobody has started yet is `Open`, not a separate
  not-started state; `Archived` is frozen until unarchived.

## 2. Key architectural decisions and trade-offs

- **Layered modular monolith.** API → Application → Domain; Infrastructure
  plugs into the Application/Domain abstractions. Not microservices: nothing
  needs independent scaling, deployment, team ownership, or availability
  boundaries, so one deployable stays simple to run while its internal
  boundaries would still allow extraction.
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
- **Space as the collaboration and authorization boundary.**
  `Space { Id, Name, Access[], Version }`; each access entry
  `{ SubjectId, SubjectType, Permission }`, `Read < Write < Owner`. The Space
  owns membership; `Space.Version` protects only Space state — name and access
  changes.
- **Todo as an independent business and concurrency boundary.**
  `Todo { SpaceId, CreatedByUserId, …, Version }`. `CreatedByUserId` is audit
  information: authorization comes from Space membership, not from who created
  the TODO. `Todo.Version` protects that one TODO, and a TODO write never
  increments `Space.Version`, so members editing different TODOs get no false
  conflicts.
- **TODOs are separate documents, not embedded in the Space.** An embedded
  `Todos[]` would grow the Space document towards MongoDB's size limit, make
  indexing, filtering, and paging harder, put unrelated TODO writes in
  contention on one document, and turn `Space.Version` into a needless global
  lock. Each TODO references its Space by `SpaceId`.
- **Optimistic concurrency.** The client reads `{ Id, Version }` and sends that
  version with each mutation; the repository issues one atomic conditional
  replace — `_id == id AND version == expected`, within the bound Space — that
  increments the version. No match means another writer got there first: HTTP
  `409 Conflict`, and the client offers to reload rather than overwrite. No
  locks, no lost updates, no contention between unrelated TODOs. Write safety,
  not real-time synchronization.
- **Space authorization pipeline.** HTTP request → MediatR request →
  `SpaceAccessBehavior` → verify the required permission → bind the authorized
  Space to `ISpaceScope` → handler → repository → MongoDB. An
  `ISpaceScopedRequest` declares its Space and required level; the behaviour
  authorizes before the handler runs, and the repository and list reader take
  the Space from the bound scope, never from a caller-supplied argument.
  Authorization and persistence scope are structurally connected — no handler
  can forget the Space filter, and an unbound scope throws rather than reading
  every Space. Accepted race: a member removed after the check but before an
  authorized write reaches MongoDB still lands it; closing that would cost every
  writer a Space-wide lock for a case the next request corrects.
- **404 versus 403.** A non-member gets `404` for the Space and everything
  under it, so the response never confirms the identifier exists. A member
  below the level a route needs gets `403`: they legitimately know the Space
  exists.
- **Derived dependency blocking.** `IsBlocked` is computed from current
  dependency state, not persisted. A stored flag would need propagation
  whenever a prerequisite changes and risks going stale; read-time derivation
  keeps the dependencies authoritative, at the cost of an aggregation stage per
  page.
- **Recurrence transaction.** Marking the current occurrence Completed and
  inserting the next one happen in one MongoDB transaction, so neither a
  completed occurrence without a successor nor a successor beside an incomplete
  occurrence can exist. This is why Compose runs MongoDB as a single-node
  replica set.
- **Keyset pagination.** Pages are ordered by the requested sort field with
  `_id` as the tie-breaker, and the opaque cursor resumes from that pair: no
  high-offset scans, suitable for large collections, stable forward paging.
  Not snapshot pagination — a TODO whose sort field changes between requests
  may move between pages.
- **Search.** Word-prefix search over a stored `searchTokens` array,
  index-backed and combined with the existing filters and cursor. Enough to
  find a TODO by name without an Elasticsearch-style service; no relevance
  ranking or arbitrary substring matching.
- **AI assistant.** Its tools send the same MediatR commands the toolbar does
  and never touch repositories, so it inherits Space authorization and every
  business rule; the turn's Space is authorized before the model is called,
  and destructive actions wait for confirmation.

## 3. What was deliberately not built

Left out on purpose, and why — the omissions are as much a decision as the
implementations.

- **Real-time collaboration.** No SignalR, WebSocket, or SSE change channel.
  Optimistic concurrency guarantees write safety, but another user's change
  appears on the next load rather than being pushed. Not required to satisfy
  the concurrency requirement, and a large expansion of scope.
- **Invitations and groups.** An access entry already carries a subject type,
  so a group is a second type plus a lookup; invitation workflows and groups
  were excluded.
- **Space lifecycle.** No delete Space, leave Space, or move TODO between
  Spaces: sharing and revoking membership already covers the collaboration
  this application is for.
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
- **Observability.** OpenTelemetry traces, metrics, and structured production
  telemetry.
- **Production schema/index deployment.** Move index management from
  application startup into an explicit deployment/migration step.

For deeper implementation reasoning and rejected alternatives, see
`docs/decision-log-detailed.md`.
