# Decision Log

The short version, in the four parts the brief asks for. Nearly every entry
has a longer treatment in [decision-log-detailed.md](decision-log-detailed.md),
which records each decision as it was made; the mechanisms are described in
[architecture.md](architecture.md).

## 1. How I interpreted the ambiguous requirements

- **"Data should not be permanently lost when deleted."** A recoverable delete,
  not "never delete". Delete sets `deletedAt` and a `purgeAt` ninety days out;
  the document stays, normal reads exclude it, and a Trash view restores it
  under the usual version check. Physical purge is a separate maintenance job,
  designed for but not yet built.
- **"Multiple users accessing the same TODO list concurrently."** Two
  requirements in one sentence, both built. *The same list* is a **Space**: a
  named list with its own access list, so people work in one collection rather
  than each in a private copy, and membership rather than ownership decides who
  may read and who may write. *Concurrently* stays a per-TODO question: every
  TODO carries a `version`, each mutation sends the version it last read,
  MongoDB matches `_id` and `version` atomically, and a miss is a `409` the
  client resolves by reloading. No Space-wide lock, deliberately — members
  editing different TODOs never contend, and two on the same one are settled by
  that one version, whether they are two people, two tabs, or the assistant.
- **"10,000+ items without degrading."** Never load or count the collection on
  the request path: keyset pagination (opaque cursor, `limit + 1`, `_id`
  tie-breaker) over indexes that lead with `spaceId`, projected cards, and
  blocked state computed inside the aggregation rather than in memory.
- **"Archived" as a status.** *Frozen*, not one status among equals: an
  archived TODO refuses edits, dependency changes, and completion, and reopens
  only by unarchiving. The rule lives in the domain entity so the single-item
  and bulk endpoints cannot disagree.
- **"Cannot move to In Progress until all dependencies are Completed."**
  Extended to Completed as well. A missing, deleted, or archived dependency
  counts as incomplete; direct and transitive cycles are rejected on edge
  creation; a prerequisite of an active dependent cannot be deleted.
- **"Next occurrence created automatically."** In the same MongoDB transaction
  as the completion, so neither exists without the other. Schedules advance
  from the *scheduled* due date, so late completion does not drift the series;
  monthly schedules keep their anchor day (Jan 31 → Feb 28/29 → Mar 31). The
  successor copies details and schedule but not dependencies. Custom means
  every *N* days, weeks, or months.
- **Due date** is a calendar date (`DateOnly`, stored `yyyy-MM-dd`), not an
  instant, so no timezone can move a deadline to a different day.

## 2. Key architectural decisions and trade-offs

- **Layered monolith, dependencies pointing inward.** Domain has no outward
  references; Application owns use cases and the abstractions
  (`ITodoRepository`, `ITodoListReader`, `ITransactionExecutor`,
  `ICurrentUser`, `ISpaceScope`); Infrastructure implements them; API owns
  HTTP. More ceremony than one project, in exchange for handlers that test
  without a database or host. No service split: nothing has an independent
  scaling or ownership need.
- **CQRS through MediatR over one data model.** Validation, domain-exception
  translation, and request logging are pipeline behaviours every request
  inherits. Separate read/write stores were rejected as unrepayable overhead.
- **MongoDB.** Embedded dependency IDs and recurrence metadata make a natural
  document. Accepted: transactions need a replica set even locally (Compose
  starts a one-member set), and "blocked" needs a `$lookup` instead of a join.
- **Optimistic concurrency, no locks.** The version is the only token. The
  browser surfaces a `409` and offers *Reload latest version* rather than
  retrying, because a retry would guess at intent; the one exception — bulk
  status changes retry once with re-read versions — is idempotent by
  construction.
- **Access enforced by the pipeline, the Space carried ambiently.** A
  Space-scoped request names the Space it acts in and the level it needs; a
  MediatR behaviour authorizes it before the handler runs and binds the outcome
  to a request-scoped `ISpaceScope` that the repository and list reader read
  exactly the way they read the current user. No handler supplies a Space
  filter, so none can forget one, and the scope throws when read unbound, so an
  unauthorized query fails instead of quietly reading every Space. A non-member
  is answered `404`, not `403`, so the response does not confirm the identifier
  exists; a member below the level a route needs is answered `403`.
- **OIDC login, encrypted HttpOnly cookie session, no tokens in the browser.**
  Removes the XSS-token-theft class. Costs: CSRF returns, so an antiforgery
  header is a global filter; the cookie handler must be the default challenge
  so a `fetch` gets `401`, not a redirect; the Data Protection key ring becomes
  state that must outlive the container.
- **Logout ends the provider session too.** Reverses an earlier
  application-only logout, which returned to the login page while leaving the
  single sign-on session open — on a shared device, that hands the next person
  the account. Costs recorded: the ID token is persisted in the encrypted
  ticket for its `id_token_hint` (only that token, so `SaveTokens` stays off),
  and logout is a form post rather than a `fetch`, because the browser has to
  follow the redirect to the provider. Kept a `POST`, not the simpler `GET`, so
  the antiforgery filter still covers it.
- **Read-time dependency state.** `isBlocked` is computed per list read.
  Persisting it goes stale on every completion, deletion, or archive of a
  prerequisite. Cost: an aggregation stage per page.
- **Token-prefix search over a stored `searchTokens` array.** `$text` rejected
  (whole words only; relevance order fights the keyset cursor); embeddings and
  an external engine as disproportionate. Costs recorded: prefix not
  substring, paging under search is O(match set), the index is the largest.
- **Indexes created at startup by a hosted service, not a migration step.**
  Registration lives in the infrastructure module, so every host that composes
  it — the API and the integration tests alike — gets the same indexes without
  remembering to ask, and a failure aborts startup rather than serving traffic
  without them. Instances starting together are tolerated by design. Accepted:
  an index build blocks writes to its collection, and the searching query hints
  its index by name, so a host that skipped creation fails those queries
  instead of running them slowly. §4's migration step is where this moves.
- **All-or-nothing bulk operations.** With dependency chains and recurrence,
  partial success leaves a caller unable to tell what happened.
- **The assistant acts *as* the user, inside the user's own request.** Tools
  send MediatR commands only, so they inherit every API guardrail; writes bind
  to the version the model last *read*, never one it supplies. A turn names the
  Space it runs in and that Space is authorized before the model is reached, so
  a turn the caller may not make costs nothing and reveals nothing. Keys are the
  user's own, encrypted at rest, write-only. Correctness never depends on the
  model: a weaker model degrades helpfulness, not data.
- **One container serves SPA and API from one origin**, so the session cookie
  has one origin and no gateway is needed.
- **Rate limiting keyed by user, not IP.** A per-user concurrency limit on
  assistant turns and a per-user fixed-window limit on mutation endpoints,
  both partitioned by the authenticated user ID so a rejected request and its
  request log name the same caller; anonymous, read, and assistant-path
  traffic are excluded from the mutation limiter so the two policies don't
  compound. The user's own model key removes the cost concern, not the abuse
  one.

## 3. What I chose not to build, and why

- **Registration.** Identity is delegated to the OIDC provider and the
  application stores no credentials, so registration is the provider's feature
  (Keycloak's `registrationAllowed`), not the application's.
- **Real-time updates.** The version check already makes staleness *safe*, so
  push is a convenience rather than a correctness need, at the price of a second
  transport and connection management. The cost is plain in a shared list: a
  colleague's change appears on the next load, though it can never silently
  overwrite anything in the meantime. §4 says how it would be built.
- **Everything about a Space except sharing it.** Deleting a Space, leaving
  one, moving a TODO between Spaces, invitations, and groups are all absent.
  The first three need an answer for the TODOs inside — cascade, orphan, or
  re-scope one by one — that the requirement does not imply; sharing adds
  someone the user directory already knows, so nothing has to be invited; and
  an access entry is already a subject, a subject *type*, and a permission with
  `User` the only type defined, so a group is a second type plus a lookup
  rather than a change of shape. What *is* enforced: a Space always has an
  Owner, who while last can be neither removed nor demoted, and nobody removes
  themselves.
- **Migration of documents written before Spaces.** A TODO carries a `spaceId`
  where it carried an `ownerId`, and nothing backfills the older form, for the
  reason nothing converts legacy enum values: no deployment holds data that
  outlives a schema change, so a local volume is recreated.
- **Physical purge job.** Retention is enforced by `purgeAt` and the restore
  rule; removal belongs outside request handling and is the remaining slice.
- **Provider-initiated logout.** Back-channel and front-channel logout end the
  application session when the sign-out starts elsewhere in the realm. That
  needs server-side sessions keyed by the provider's session ID, because an
  encrypted cookie cannot be revoked from outside the browser holding it. With
  one application in the realm there is nowhere else for a sign-out to start.
- **Server-side assistant history.** The client echoes a windowed transcript;
  storing it is a history *feature* and would not fix the token cost.
- **Closing two windows, rather than accepting them.** An access check and the
  write it authorizes are not one atomic step, so a member removed in between
  still lands that write; sealing it costs every writer a Space-wide lock to
  correct a case the next request corrects anyway. And finding a colleague is a
  bounded disclosure rather than none: signed-in callers, at least two
  characters, at most ten answers, the caller excluded, and only people who
  have signed in at least once — the directory cannot be walked, but a real
  colleague is findable by name or address.
- Also rejected, for reasons in §2: persisted blocked state, an `IUnitOfWork`,
  browser-held OIDC tokens, and partial-success batches.

## 4. What I would do differently with more time

- **Real-time list updates over MongoDB change streams.** The replica set and
  the versions are already there; a per-Space change feed over SSE could drive
  the existing refresh path with no new consistency rules, and would turn a
  shared list from "correct on the next load" into "correct as you watch".
- **A scheduled retention purge**, whose Space-spanning repository path is
  the one exception reserved in the "no bound Space, no query" rule but not yet
  written.
- **Observability beyond logs:** OpenTelemetry metrics and traces for request
  latency, Mongo command timing, and assistant token usage.
- **Component-level frontend tests**, for faster feedback than the browser
  suite gives on UI state.
- **A full-stack Compose profile** that also runs the application image, so
  one command starts everything instead of three terminals.
- **A migration step instead of startup work.** Nothing has been deployed
  anywhere its data outlives a schema change, so local databases are recreated
  instead. A real deployment target makes that a migration step rather than a
  `docker compose down --volumes`, and index creation leaves startup for the
  same step, so a build no longer blocks a collection's writes on every start.
