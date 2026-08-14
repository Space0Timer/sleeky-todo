# Architecture

The application uses a layered monolith:

```text
React
  -> ASP.NET Core API
  -> Application commands and queries        <- Assistant tools also enter here
  -> Domain and infrastructure
  -> MongoDB
```

The Application layer owns persistence and time abstractions. Infrastructure supplies their runtime implementations, keeping command handlers testable and independent of MongoDB and the system clock.

The Assistant is a fifth project sitting beside the API rather than beneath it. It sends the same commands and queries a controller does, so it enters the stack at the same point and inherits everything below. Every provider SDK dependency stops there, which is what keeps Application and API free of them.

## React client and persisted workflow

The React client uses a typed API module for list, detail, create, update,
status, dependency, delete, and restore requests. It parses Problem Details in
one place and distinguishes validation, domain-rule, concurrency, not-found,
network, and unexpected failures. Serilog and backend implementation types do
not cross the HTTP boundary.

The main screen is backed by `GET /api/todos`; it does not keep a browser-only
TODO collection. Active, Archived, and Trash tabs select the matching server
scope. Changing scope, filters, sort field, or direction starts a new first-page
request without a cursor and replaces the displayed items. Load More sends the
opaque `nextCursor` and appends the returned page.

List responses remain projections. Full TODO details are loaded only when the
user opens management controls or when a blocked card needs prerequisite names.
Each mutation of an existing TODO uses the version from the latest loaded
representation and then refreshes the persisted list. A concurrency response is
never overwritten silently: the UI displays the conflict and Reload Latest
Version replaces stale state from the server.

The assistant panel sits beside the list and refreshes it through the same
callback the bulk toolbar uses, because its writes are the same writes. Turn
events are read off the `fetch` body by a small parser beside the API module;
the transcript it holds is opaque to the client, which only carries it back.

The filter panel leads with a search box. Its own state holds what is typed;
only the debounced value is merged into the filters the list reads, so a
keystroke cannot invalidate a cursor the Load More button is still holding. The
merge returns the existing filters object when the value has not changed, which
is what stops the mount and the Clear reset from each firing a second identical
request. Load More is hidden while a first page is in flight, because the cursor
still on screen belongs to the page being replaced.

Dependency selection is no longer bounded by what the client has loaded. The
picker sends the typed text to the same list endpoint the page uses, scoped to
active TODOs and sorted by normalized name, and the server matches it. Only the
two predicates the server cannot know stay in the client: the card excludes
itself and the prerequisites it already has. A selection that stops being
offered as the list narrows is cleared, so Add cannot send an identifier the
picker no longer shows. While a fetch is in flight the previous options remain
visible and selectable and the group is marked busy, rather than flickering
empty on every pause in typing.

The match is by token prefix rather than by substring, so a word has to be
typed from its start. That is the deliberate cost of making the match
index-backed; see the decision log.

## Authentication and session boundary

This section records the boundary the authentication slice holds, because the
ownership, transport, and test decisions all rest on it.

Login uses OpenID Connect, and the application session is an ASP.NET Core
encrypted cookie. The React client never receives or stores an access, ID, or
refresh token:

```text
React /login
  -> GET /api/auth/login
  -> OIDC provider
  -> /signin-oidc
  -> resolve or create the internal user
  -> encrypted HttpOnly session cookie
  -> redirect to a validated local return URL
```

The cookie handler is both the default authentication scheme and the default
challenge scheme; OpenID Connect is challenged explicitly and only by
`GET /api/auth/login`. The challenge scheme decides what an unauthenticated API
request receives, so this is a functional choice rather than a stylistic one.
With OpenID Connect as the default challenge, a client `fetch` would be answered
with a redirect to the provider instead of a status code. The cookie handler's
redirect events are therefore replaced so API requests receive `401` and `403`
directly, and the client treats `401` as "start the login flow" rather than
following a cross-origin redirect it cannot complete.

The session cookie carries a minimal encrypted ticket: the internal user ID, a
display name, and authentication metadata. Provider tokens are not saved into
the ticket, the browser, or any client-readable storage.

### Development callback routing

The OpenID Connect handler derives its `redirect_uri` from the incoming request,
and its correlation cookie is written for the origin the browser is using. In
development the browser runs on the Vite origin while the API listens on its own
HTTPS port, so the callback paths must be proxied alongside `/api` and `/health`
and the API must observe the client host rather than its own. Without both, the
provider returns the browser to the API origin, where the correlation cookie
does not exist and the post-login redirect lands outside the client application.

### Antiforgery

A cookie-authenticated API is exposed to cross-site request forgery, so
state-changing requests carry an antiforgery token in a request header.
Validation is registered as a global filter rather than per endpoint: leaving a
mutation unprotected should require a deliberate opt-out instead of a remembered
opt-in.

Antiforgery tokens are bound to the authenticated identity, so a token issued
before login fails validation after it. The client therefore refreshes its token
whenever authentication state changes: on startup, after login completes, and
after logout. The antiforgery cookie may be readable by JavaScript; the session
cookie remains HttpOnly and is the only authentication credential.

### Logout

Logout validates the antiforgery token, deletes the application cookie, and
returns `204`. It deliberately does not call the provider's end-session
endpoint, so the provider session can outlive the application session and a
subsequent login may complete without a new credential prompt. A `fetch` cannot
follow a redirect into a provider logout page in any case, so the client clears
its own state and navigates to `/login` itself.

## Ownership boundary

Every TODO carries an `OwnerId` holding the internal user ID, persisted as a
standard BSON UUID like the other backend-owned identifiers. Application code
reads the current user through an `ICurrentUser` abstraction; handlers never
touch `HttpContext`.

Ownership is enforced where the query is built rather than at each call site.
Infrastructure injects `ICurrentUser` into the repository and list reader and
applies the owner predicate inside the shared identifier, mutation, and list
filters, so reads, existence checks, batch loads, dependency lookups, graph
traversal, active-dependent checks, mutations, and cursor pages are scoped by
construction. A future query cannot omit the filter by forgetting it, because no
handler supplies it. The repository refuses to run without an authenticated
user; the retention purge path is the deliberate exception, because it is a
maintenance operation that spans owners.

A TODO belonging to another user is reported as `404` rather than `403`, so the
response does not disclose that the identifier exists.

The assistant is a second actor on this boundary and needed no rule of its own.
It dispatches the same commands from inside the caller's request, so the owner
predicate is applied by the same filters; an integration test drives its tool
layer against another owner's TODO and gets the same nothing a controller would.

Sort and lookup indexes gain `ownerId` as their leading key, since every query
now filters on it before any scope, sort, or dependency term. The retention
`purgeAt` index stays owner-independent to match the purge path. The index
initializer creates indexes but does not remove superseded ones, so the
replaced index names are dropped explicitly before creation; otherwise an
existing deployment would retain unused indexes that still cost write time.

`owner_active_search_tokens` puts its array key last — `ownerId`, `deletedAt`,
then `searchTokens` — unlike `owner_active_dependency_ids`, which carries its
array second. The difference is deliberate: a search matches owner and scope
exactly and then scans a range of tokens, so the equality keys have to precede
the range for the bounds to be tight, while a dependency lookup matches an
exact identifier inside the array and does not pay the same cost.

**Operationally, a missing search index breaks search alone, and loudly.** The
list query hints that index by name whenever there is something to search for,
and a hint naming an index that does not exist fails the query rather than
falling back to a scan. If search returns 500 while every other list, filter,
and sort keeps working — after a hand-dropped index, a restored database, or a
harness that drops the database behind a running instance — check that
`owner_active_search_tokens` exists. Restarting the application rebuilds it.

Recurring occurrences inherit the completed occurrence's owner through the
domain entity, so the transactional insert requires no separate ownership rule.

## Persistence boundary

Application handlers depend on the public `ITodoRepository` abstraction. Infrastructure registers `MongoTodoRepository` as its implementation:

```text
Application handler
  -> ITodoRepository
  -> MongoTodoRepository
  -> IMongoDatabase / IMongoCollection<TodoDocument>
  -> MongoDB
```

`TodoDocument`, its serializer, and its mapper are internal Infrastructure details. They are not exposed to Application or test projects. Repository integration tests exercise the public `ITodoRepository` contract and inspect raw BSON only when the persisted representation matters.

A separate `MongoDbContext` wrapper is intentionally not used. With one database and one collection it only duplicated `IMongoDatabase.GetCollection`; the repository owns collection access directly.

Assistant provider settings are a second collection behind the same boundary:
`IAssistantSettingsRepository` in Application, `MongoAssistantSettingsRepository`
in Infrastructure, keyed by the owning user so a user has one record and no
separate document identity. It stores the API key as ciphertext and cannot
decrypt it; encryption belongs to the assistant, which is the only layer that
needs the plaintext.

List queries use a separate `ITodoListReader` implemented by
`MongoTodoListReader`. Its aggregation applies scope and field filters, joins
dependency documents, computes blocked state, applies the deterministic cursor,
and fetches `limit + 1`. Application owns cursor encoding and validation so the
HTTP and persistence layers share one filter-bound cursor contract without
exposing BSON types.

Search reaches the reader as `TodoListCriteria.SearchTerms`, already tokenized.
Splitting stays above the persistence boundary, so Infrastructure never learns
the tokenizer's rules and cannot drift from what the write path stored. The
reader hints the search index only when that list is non-empty; a query with
nothing to search for is left to the planner exactly as before. Terms are
ordered longest first in the hope that the most selective one receives the
index bounds, which is a heuristic rather than a guarantee — an explain-based
integration test asserts the bounds themselves, so a server or driver upgrade
that changes which predicate is chosen fails there rather than silently
becoming a scan.

The cursor's filter signature gains a seventh component carrying the tokens,
appended only when there are any. An unsearched query therefore hashes exactly
as it did before search existed, and cursors already in flight survive the
deployment that introduced it.

### Enum storage

`TodoStatus` and `TodoPriority` are stored as BSON `int32` values. Their explicit
numeric values are the business sort order:

```text
TodoPriority: Low=0, Medium=1, High=2
TodoStatus: NotStarted=0, InProgress=1, Completed=2, Archived=3
```

The numeric values are persistence contracts and must not be renumbered. New
values are appended only with a deliberate data migration. Integer storage lets
MongoDB filter, cursor-page, and sort directly on the persisted fields, so the
reader does not need temporary rank fields or `$switch` expressions.

Legacy string values are validated and converted by
`MongoDbEnumStorageMigrator` during startup before index initialization. The
migration is idempotent, rejects unknown values before changing data, and is
not a mixed-version rollout: old writers must be stopped first.

### Search tokens

Each TODO stores `searchTokens`, the deduplicated lowercase words of its name
and description. `SearchTokenizer` in Domain produces them, splitting on runs
of anything that is not a letter or digit and truncating a token at 64
characters. The entity exposes them as a computed property rather than stored
state, so no creation, rehydration, edit, or recurring occurrence can persist
tokens that disagree with the text they came from. Every repository write is a
full-document insert or replace through `TodoDocumentMapper`, which is what
makes that guarantee reach persistence; a repository write must not be narrowed
into a partial `$set` that leaves the field behind.

The query side calls the same tokenizer on what the user typed, so a stored
token and a typed term are produced by one set of rules by construction. Each
term becomes an anchored, case-sensitive regex on `searchTokens`, and all of
them must match; both sides are already lowercased, and a case-insensitive flag
would discard the index bounds.

`MongoDbSearchTokensMigrator` backfills documents written before the field
existed, filtering on `searchTokens` being absent and writing per-document
`$set`s in batches. It is registered between the enum migrator and the index
initializer so the tokens exist before the index covering them is built. Its
filter is unindexed and therefore costs one collection scan per start, which
matches what the enum migration beside it already costs.

### Fluent aggregation design

The reader uses typed MongoDB filters, cursor comparisons, sorting, and limits.
Raw BSON stages are limited to the self-lookup, dependency count, and computed
projection where the aggregation expression is clearer than a driver builder.

For requests without a dependency-state filter, the pipeline is:

```text
scope and field match
  -> cursor match
  -> deterministic sort
  -> limit
  -> completed-dependency lookup
  -> incomplete-count calculation
  -> list-row projection
```

Sorting and limiting before the lookup bounds dependency work. Blocked and
unblocked requests calculate dependency state before filtering, sorting, and
limiting so a page is not incorrectly truncated before the requested state is
selected.

The cursor predicate is equivalent to
`sortField > lastSortValue OR (sortField == lastSortValue AND _id > lastTodoId)`;
both comparisons reverse for descending order. The persisted sort field and
`_id` are sorted in the same direction for deterministic, indexable pages.

### Dependency calculation

The list lookup joins only dependency IDs that are present, not deleted, and
completed, and projects only `_id`. Full dependency documents are not copied
through the aggregation. The list projection calculates:

```text
incompleteDependencyCount = dependencyIds.Count - completedDependencyIds.Count
isBlocked = incompleteDependencyCount > 0
```

Missing, deleted, archived, and unfinished dependencies therefore remain
incomplete, matching the status-transition rule. The values are calculated at
read time rather than persisted so dependency mutations cannot leave a stale
blocked flag or count.

## Optimistic concurrency

Every mutable TODO carries a numeric version. Update, soft-delete, restore,
dependency, and status requests include the version last read by the client.
The assistant holds the same rule against a different actor: its tools accept
identifiers and bind the version the model last read, so what reaches this check
is still the version whoever acted had actually seen.
Backend-owned TODO, dependency, recurrence-series, and cursor tie-breaker
identifiers use `Guid` throughout Domain, Application, API, and Infrastructure.
MongoDB stores them as standard BSON UUIDs (binary subtype 4), including TODO
IDs, dependency IDs, recurrence-series IDs, and cursor tie-breaker IDs. The
JSON/React contract remains unchanged because ASP.NET Core represents each
`Guid` as its canonical UUID string at the HTTP boundary. Persistence document
models ignore unknown BSON elements to support additive rolling schema changes.

The repository performs each mutation as one MongoDB `FindOneAndReplace` operation with a filter equivalent to:

```text
_id == todoId AND version == expectedVersion
```

Update and soft-delete also require an active document. Restore requires a deleted document. The replacement increments the version by one, and `ReturnDocument.After` returns the actual persisted state.

If the filter matches nothing, the repository throws `ConcurrencyConflictException` itself, because it is the only component that knows both the TODO ID and the expected version. That expected version is the aggregate's own `Version`, which no domain method mutates, so callers never pass it separately. This covers the race between the handler's initial read and its write. `UpdatedAt` remains ordinary data and is never used as the concurrency token.

Integration tests issue simultaneous mutations with the same version and verify that exactly one succeeds for update/update, update/delete, and restore/restore races.

## Transactional recurring completion

Only a real transition from a non-completed state into `Completed` raises a
`TodoCompletedDomainEvent`. Completion runs through `ITransactionExecutor`:

```text
ChangeTodoStatus handler
  -> ITransactionExecutor.ExecuteAsync
     -> start MongoDB session transaction
     -> versioned replacement of current occurrence
     -> dispatch TodoCompletedDomainEvent in-process
     -> calculate next date from scheduled due date
     -> insert next occurrence through the same session
     -> commit
```

`ITransactionExecutor` takes the work as a lambda and runs it as one atomic
unit, committing when it returns and aborting when it throws. It is deliberately
not a unit of work: nothing is tracked, deferred, or coordinated, and repository
writes stay immediate. The MongoDB repository reads the scoped transaction
context and uses the active session for both the replacement and the
event-handler insert, so anything running inside the operation joins the
transaction without knowing it exists. Any event-handler or write failure aborts
it.

An aborted transaction surfaces as `TransactionConflictException`, which carries
no resource identifier because the conflicting document is not always the one
the caller named. A unique partial index on owner, series ID, and occurrence
number complements optimistic concurrency and prevents duplicate next
occurrences.

Schema and data bootstrap never runs inside this boundary. MongoDB forbids index
creation and removal in a transaction, and the index initializer and enum
migrator are idempotent hosted services that already tolerate concurrent
instances, so partial application is safe and self-heals on the next start.

The recurrence calculator preserves a stored monthly anchor rather than adding
months to a previously clamped date. Thus January 31 becomes February's final
day and then March 31. New occurrences copy descriptive fields, priority, and
the schedule, but deliberately start with no dependency IDs.

## Bulk status changes and deletion

`PUT /api/todos/status` and `DELETE /api/todos` mirror their single-item
counterparts at collection level, so the target status stays the discriminator
rather than becoming a separate action name. A request carries up to 100 unique
selections, each with the version the client last read.

```text
bulk handler
  -> one GetByIds load, returned in request order
  -> reject a missing id (404) before comparing any version
  -> reject every stale version at once (409, naming each)
  -> per-action validation
  -> apply domain methods in memory
  -> one ordered BulkWrite inside a transaction
```

A selected TODO already at the target status is a no-op, exactly as for a single
request: it comes back with its version unchanged and no document is written. A
batch that changes nothing writes nothing, and a batch that writes a single
document skips the transaction, so bulk requests still work against a standalone
deployment.

Completion treats a dependency as satisfied when it is already completed or is
part of the same batch, which lets a prerequisite and its dependent complete
together; only dependencies outside the batch are loaded, in one query. Deletion
inverts the relationship: one query finds active, non-archived dependents that
point at the selection and are not themselves selected, so deleting a
prerequisite together with its dependent is allowed while deleting the
prerequisite alone is not.

Recurring completions build their next occurrences through the shared
`IRecurringOccurrenceFactory` and join the same batch. The bulk path
deliberately bypasses `IDomainEventDispatcher`, whose handler would issue a
separate insert per occurrence.

Because a bulk write reports counts rather than documents, written versions are
computed as `version + 1` and checked against the matched and inserted counts
before the transaction commits. A duplicate key arrives as a bulk write error
rather than the single write error the transaction executor recognises, so the
repository maps it back to the offending TODO through the write error index.

The browser is no longer the only caller of these batches. The assistant sends
the same commands and carries its own retry policy; see the assistant section
below, and "Two conflict policies" in the decision log for why the two are not
consolidated.

## Assistant

The assistant turns natural language into the same bulk writes the toolbar
issues. It runs in process and synchronously, inside the caller's own
authenticated request, so `ICurrentUser` resolves from that HTTP context and the
assistant acts as the user by construction rather than by an impersonation rule.

```text
POST /api/assistant/turns  (server-sent events)
  -> AssistantTurnRunner        resolves a provider, replays the transcript
  -> IChatClient                Anthropic or an OpenAI-compatible endpoint
  -> TodoTools                  six AIFunctions over commands and queries
  -> MediatR                    validation, domain rules, logging, ownership
```

Tools send commands and queries only, never repositories, so every call inherits
the guardrails the HTTP path has. Nothing in the tool layer reaches persistence
directly, which is what makes "the assistant cannot do what a browser could not"
a structural property rather than a review convention.

Reads return versions and writes take identifiers. A per-turn ledger binds each
write to the version the model last read, keeping "version sent" equal to
"version the actor last saw" — the rule the browser already holds. The ledger is
seeded by scanning the echoed transcript, because the server keeps no
conversation history and a model will not re-read what is still in its context.

The conversation is windowed before it is replayed. The client holds the
transcript and echoes it back each turn, so nothing bounded it: a long
conversation grew the request body, the model's context, and the tokens every
later turn paid to replay it. `TranscriptWindow` keeps the opening message and
the most recent `Assistant:TranscriptMaxMessages`, and the windowed conversation
is both what the model is sent and what the turn hands back, so the copy the
client holds stops growing too. What the person sees is unaffected, because the
client renders the chat log from turn events rather than from the transcript it
carries.

The ledger is seeded from the windowed conversation rather than from what
arrived, so a read that fell out of the window takes its version with it and the
model cannot write against a version it can no longer see. The window opens
after any orphaned tool result, since a result whose call has been trimmed away
is a message providers reject.

Conflict handling sits above the dispatch in `BulkConflictPolicy`, not in the
handlers, which are shared with the HTTP path: a retry inside one would make the
browser's writes silently retry too.

Deletion proposes and stops. The tool publishes the selection's state, ends the
turn, and the confirming turn executes with exactly the versions it displayed,
so a replayed confirmation fails on the moved version. The gate is implemented
here rather than through a framework's approval feature, so it behaves
identically on every provider.

Provider settings are per user. The API key is encrypted through ASP.NET Data
Protection before it reaches persistence and decrypted only where a provider
client is built, so the repository stores a string it cannot read. No route
returns a key: it can be replaced but never retrieved.

## Soft delete and restore

Soft delete is a domain transition rather than a physical MongoDB delete:

```text
Active TODO
  -> deletedAt = current UTC time
  -> purgeAt = deletedAt + 90 days
  -> updatedAt = deletedAt
  -> version = version + 1
```

Normal reads and existence checks add `deletedAt == null` to their repository filter. Restore deliberately includes deleted records, validates that the retention boundary has not been reached, clears `deletedAt` and `purgeAt`, and persists the next version atomically.

Deletion state is valid only when `deletedAt` and `purgeAt` are either both null or both present, with `purgeAt` later than `deletedAt`. Restore cannot use a timestamp before deletion.

The background job that physically removes records after `purgeAt` is separate
from this recoverable lifecycle. Before deletion, the application asks the
repository whether an active, non-archived TODO depends on the target and
rejects the transition when one exists.

## Dependency graph and status rules

Dependency mutations are application commands backed by aggregate methods on
`TodoItem`. The add path verifies an active target, then uses a breadth-first
graph service to determine whether the proposed target already reaches the
source. Each frontier is loaded with one `GetByIdsAsync` call, and a visited set
guarantees termination for malformed legacy graphs.

Status transitions use a dependency evaluator that batch-loads all direct
dependencies, including deleted documents. Missing, deleted, archived, or
non-completed dependencies contribute to the incomplete count and block entry
to `InProgress` or `Completed`. The list reader independently projects the same
blocked semantics for query responses and filtering.

All dependency and status writes retain the source TODO's expected version in
the repository filter. A graph check is therefore followed by the same atomic
optimistic write used by the CRUD commands.

## HTTP and error boundary

Controllers translate API contracts into MediatR requests and select success
status codes; they contain no domain or persistence behavior. FluentValidation
runs before handlers, and a domain-rule pipeline behavior converts domain
exceptions into the application-facing `DomainRuleException`.

The global API exception handler produces RFC Problem Details. It maps
`NotFoundException` to 404 and both `ConcurrencyConflictException` and
`DomainRuleException` to 409. Validation errors use a predictable camel-cased
`errors` dictionary. All problem responses include the request path and a
`traceId`.

The assistant turn endpoint is the one route that does not end in that contract.
It streams server-sent events, so a failure after the first event cannot become
a Problem Details body — the status line is already sent. The turn instead ends
by faulting the stream, and the client reports a turn that stopped. Failures
before the stream opens, and every other assistant route, answer as above. It is
a POST with the stream read off the response body rather than an `EventSource`,
because antiforgery is a global requirement here and `EventSource` can only GET.

## Logging boundary

Serilog is configured only by the API host. API, Application, and Infrastructure
classes emit events through Microsoft `ILogger<T>`, which supplies a stable
source category while keeping those layers independent of the logging provider.
The host uses a bootstrap logger for startup failures and replaces it with the
configuration-driven Serilog pipeline after dependency injection is available.

One HTTP completion event records method, path, status, duration, request ID,
and trace ID. A MediatR behavior records the application request type and
successful handling duration, and Infrastructure records successful MongoDB
index initialization and each startup migration that changed data. Successful
health-check completion events are reduced to
Debug, while unhealthy health checks remain Warning events. A recurring TODO
completion records the series, completed TODO, and newly created TODO identifiers
only after the transaction commits. Successful TODO create, update, status,
dependency, delete, and restore mutations also emit Information audit events
containing identifiers, versions, and operation-specific metadata.
Expected validation, not-found, domain, and concurrency responses are not logged
as exceptions; the global exception handler emits the single Error event for an
unexpected exception with its trace ID, method, and path, while its HTTP 500
completion is a Warning.

Authenticated request events are enriched with the internal user ID so audit
events can be attributed without a second lookup. The authentication slice adds
events for successful login, failed login, logout, and first-time user creation.

Commands the assistant issues open a logging scope carrying `RequestOrigin`, so
an audit event answers whether a person or the assistant made a change without
the request pipeline needing to know the assistant exists.

Logging excludes request bodies, TODO descriptions, cursor query values, and
MongoDB connection strings. It also excludes a user's provider API key, which is
handled only where a provider client is built and is stripped from any error a
connection probe reports. It excludes provider tokens, cookie values,
client secrets, antiforgery tokens, and the raw OIDC subject, because the
internal user ID already identifies the actor without carrying a provider
credential into the log stream. Structured events use stable event IDs and named
properties rather than interpolated payloads. Code uses direct typed logger calls
such as `this.logger.LogInformation(eventId, template, values)` so the emitting
class and event shape remain explicit without provider-specific APIs.

## Startup responsibilities

Each layer exposes a dependency-injection extension. API startup composes those
extensions, while Infrastructure validates MongoDB settings, registers the
repository and health check, and runs its schema and data bootstrap through
hosted services: the enum migration, the search-token backfill, and index
initialization, in that registration order. This keeps `Program.cs` limited to
composition and application startup.

`AddAssistant` binds provider options without validating them on start: an
application-level API key is optional, and a deployment where every user brings
their own is a valid one. Data Protection therefore carries a second
responsibility beyond session cookies — it encrypts stored provider keys — so a
deployment without a durable key ring loses saved keys on restart rather than
only signing users out. The keys stay in the database and are reported as
unusable until replaced.

The image writes the ring to `/keys` and creates that directory owned by the
user it runs as, because a named volume takes its ownership from the directory
it covers: mounted over a path that does not exist, it arrives owned by root and
the application cannot write it. Persisting the ring is therefore a mount rather
than a mount plus a setting, and the container smoke test asserts the mounted
directory is writable so the ownership rule fails in CI rather than at a user's
first login.

Constructors fail fast with `ArgumentNullException` for every required injected
dependency. This makes direct construction and registration mistakes fail at
the composition boundary instead of later during a request. The optional
`MongoTransactionContext` parameter on `MongoTodoRepository` is deliberate:
normal dependency injection supplies the scoped context, while direct repository
construction can fall back to a new context when no transaction is required.
