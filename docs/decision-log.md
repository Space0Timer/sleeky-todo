# Decision Log

This document records architecture decisions, requirement interpretations,
trade-offs, known limitations, and intentionally omitted features.

## Layered monolith

The backend is a layered monolith split into Domain, Application,
Infrastructure, and API projects. This keeps business rules independent of HTTP
and MongoDB without introducing distributed-system overhead. Dependencies point
inward: API composes Application and Infrastructure, Infrastructure implements
Application abstractions, and Domain has no outward dependencies.

The React client is a separate build artifact but consumes the same HTTP API.
Additional deployable backend services should only be introduced when an
independent scaling or ownership requirement justifies them.

## CQRS with MediatR

HTTP controllers send commands and queries through MediatR. Each request has a
single handler, and cross-cutting validation and domain-exception translation
run as pipeline behaviors. Commands and queries share one MongoDB data model;
CQRS here separates use-case code rather than introducing separate read and
write databases.

This keeps controllers thin and handlers independently testable while avoiding
an event bus or messaging infrastructure for an in-process application.

## Persisted React workflow

The React client treats the API as the source of truth rather than maintaining
a session-only TODO collection. Scope, filter, and sort changes replace the
current cursor chain; Load More is the only operation that appends a server
page. Full TODO details are fetched on demand for management, while list cards
use the smaller projection returned by the list endpoint.

Every update, status, dependency, delete, and restore mutation carries the last
observed version and refreshes the list after success. A stale-version response
is shown explicitly and requires a Reload Latest Version action, preserving the
same no-silent-overwrite rule as the API.

The dependency selector loads at most 100 active TODO projections ordered by
name and searches within that bounded set. This keeps browser memory and network
work bounded without adding a new API contract, at the cost of not exposing
candidates beyond that first page. Server-side dependency search is the chosen
follow-up if larger datasets must be supported.

## MongoDB

MongoDB is the persistence store because TODO documents naturally contain
embedded and evolving fields such as dependencies and recurrence metadata. The
application uses a single-member replica set locally so recurring completion
can use MongoDB transactions without changing development topology.

MongoDB-specific documents, BSON serializers, indexes, and repository behavior
remain inside Infrastructure.

## Identifier representation

Domain, Application, API, and Infrastructure use `Guid` for backend-owned TODO,
dependency, recurrence-series, and cursor tie-breaker identifiers. MongoDB
stores those values as standard BSON UUIDs (binary subtype 4) rather than text,
which keeps identifier fields and index keys compact and gives them an explicit
cross-driver representation. ASP.NET Core continues to expose UUIDs as
canonical strings in JSON, so the React boundary remains transport-friendly.

String and binary UUID values are deliberately not mixed. Existing string-ID
documents require a collection migration/reinsert because MongoDB `_id` values
cannot be changed in place. OIDC subject identifiers are provider-owned and
remain strings rather than being coerced into this backend identifier policy.

## Date-only due dates

Due dates are calendar dates rather than instants, so Domain and Application use
`DateOnly`. MongoDB stores each due date as an ISO `yyyy-MM-dd` string through an
explicit BSON serializer, and the API uses the matching JSON date format. This
avoids timezone conversion changing the day while retaining lexicographic date
ordering.

## MongoDB repository boundary

Application code depends on `ITodoRepository`, while Infrastructure provides `MongoTodoRepository`. The repository uses `IMongoDatabase` directly rather than introducing a thin `MongoDbContext` wrapper. MongoDB documents and mapping types remain internal so persistence-specific BSON concerns cannot leak into Application or Domain code.

Integration tests use the public repository contract. Exact storage representations are checked as raw BSON after writing through the repository, avoiding both `InternalsVisibleTo` and a public persistence document type.

## Persisted enum representation and migration

`TodoStatus` and `TodoPriority` are persisted as BSON `int32` values because
their explicit numeric values represent the required business ordering. This
lets MongoDB use the existing sort indexes directly and removes the temporary
rank expressions previously needed by list queries.

The startup migrator accepts known legacy names and already-migrated integers,
rejects unknown or malformed values before modifying either field, and updates
known names idempotently. This is an in-place deployment step, not a mixed-
version rolling migration: old writers must be stopped and a recoverable
database backup taken first. A rollback to string-enum binaries requires an
explicit reverse migration; the application does not perform that contraction
automatically.

## Optimistic concurrency

The numeric `version` field is the sole concurrency token; `updatedAt` is not used for concurrency. Clients send the version they last read with every mutable request.

MongoDB mutations atomically filter by TODO ID and expected version, write version `N + 1`, and return the persisted document. A missing match indicates a stale write, which the repository reports by throwing `ConcurrencyConflictException`. The expected version is the aggregate's own `version`; no domain method mutates it, so callers do not pass it alongside the aggregate.

This design avoids locks and prevents lost updates. Concurrent integration tests verify that exactly one mutation succeeds when two writers submit update, delete, or restore operations using the same version.

## Transaction boundary

Operations that must write more than one document run through `ITransactionExecutor.ExecuteAsync`, which takes the work as a lambda, commits when it returns, and aborts when it throws. Scoping the boundary this way means no caller can forget to commit or roll back, and repository calls made inside the operation join the transaction through the scoped session context.

The name is deliberate: this is not a unit of work. Nothing is tracked, deferred, or coordinated, and repository writes stay immediate, so responses are read back from the server rather than predicted. Calling it `IUnitOfWork` would promise the change tracking and `SaveChanges` semantics the design does not have.

Conflicts are reported where their detail is known. The repository throws `ConcurrencyConflictException` carrying the TODO ID and expected version, because it issued the filter. The executor throws `TransactionConflictException` without an identifier, because an aborted transaction does not say which document lost. The API maps both to 409, which always means "re-run the read-modify-write" — deliberately including transient cluster events such as a primary stepdown, which are surfaced to the client rather than retried server-side.

Schema and data bootstrap is excluded from this boundary. MongoDB forbids index creation and removal inside a transaction, hosted services run before the request pipeline exists, and the initializers are idempotent, so there is nothing to roll back.

## Ninety-day recoverable deletion

“Data should not be permanently lost when deleted” is interpreted as requiring a recoverable soft-delete period. Delete records the UTC deletion time and a purge time exactly 90 days later; it does not remove the MongoDB document. Normal queries exclude deleted records.

Restore is allowed strictly before the purge timestamp and requires the latest version. At the purge boundary the record is expired and cannot be restored. A later retention job will physically remove expired records; that cleanup is intentionally separate from request handling.

Deleting a TODO that is still required by an active, non-archived dependent is
rejected. Archived and deleted dependents do not prevent deletion.

## Dependency graph and blocked transitions

Each TODO stores outgoing dependency IDs. Adding an edge requires an active
target and rejects self-dependencies, duplicates, and direct or transitive
cycles. Cycle detection traverses the graph breadth-first and batch-loads each
frontier, avoiding one repository call per node and terminating safely even if
legacy data already contains a cycle.

Blocked state is evaluated from one batch read of the source TODO's dependency
IDs. A missing, deleted, archived, or non-completed dependency blocks the
source. Blocked TODOs cannot enter `InProgress` or `Completed`; other valid
status transitions remain available. Dependency and status commands use the
same optimistic version contract as the CRUD commands.

## Archived TODOs are frozen

Archiving is treated as putting a TODO beyond further work rather than as one status among equals. An archived TODO rejects edits, dependency changes, and completion; only unarchiving to `NotStarted` or `InProgress` reopens it. Soft delete stays available so archived records can still be cleaned up, and archiving itself remains reachable from every other status.

The rule lives in the domain, so the single-item and bulk endpoints inherit identical behaviour rather than agreeing by convention. It tightens `PUT /{id}`, `PUT /{id}/status` towards `Completed`, and dependency changes on archived TODOs into 409 responses.

## Bulk actions mirror the single-item API

Bulk endpoints reuse the vocabulary of the single-item routes — `PUT /api/todos/status` and `DELETE /api/todos` — rather than introducing an action name and a discriminator enum. Widening bulk status changes to reopening or unarchiving later becomes a validator change instead of a new endpoint. Literal segments outrank route parameters, and the `{id}` routes carry `:guid` constraints so that precedence is enforced rather than incidental.

A batch is all-or-nothing. Every selected TODO is loaded in one query, a missing identifier fails as 404 before any version is compared, and every stale version is reported together. Validation runs against the whole selection, so completing a prerequisite alongside its dependent succeeds while completing a TODO blocked by something outside the selection fails the entire request. Partial success was rejected because dependency chains and recurring occurrence creation would leave callers unable to tell what actually happened.

Both delete routes answer 200 with a body, because the response carries the new version and deletion timestamp that a following restore needs. The single-item route returned 204 until the client began to need that version.

## Reading a selection without disturbing the list

`GET /api/todos/selection` reports the current state of specific identifiers. A client holding a selection that the server has just rejected needs to know what changed and what vanished, and it needs that without refreshing the list, because a selection resolves its versions from what is on screen: refreshing underneath one would send versions the user never saw.

The list route cannot answer this. Its scope defaults to Active, so a TODO that drifted into Archived reads as vanished; its cursor is bound to a filter signature that an identifier set does not have; and covering a selection by paging is unsound at any page size.

The read is found-only. Identifiers that no longer resolve are absent from the response rather than failing it, which is the opposite of the batch loader, where a missing identifier fails the whole request as 404. The two differ because their jobs differ: a write must refuse to act on a selection it cannot fully honour, while a probe exists precisely to report what is no longer there.

Soft-deleted TODOs still resolve. The trash lists them and a selection there is restorable, so hiding them would leave a conflict in that scope permanently unrepairable — the probe would report every selected TODO as vanished. Only what is purged, or owned by someone else, is absent. `deletedAt` on each item tells the caller which state it is in.

## Restoring in bulk

Restoration is the one batch whose selection is deleted by definition, which the write path did not anticipate. Three separate places filtered soft-deleted documents out by default: the batch loader, the selection probe, and the batch write's own filter, whose `deletedAt == null` clause matched nothing and reported the miss as a concurrency conflict. Each now states what it expects rather than assuming an active document.

The batch write asserts the stored document *is* deleted, exactly as the single-item restore does, so a TODO that someone else already restored fails the batch instead of being written over. There is no dependency gate: a restored TODO blocks nothing, and its own prerequisites are evaluated when it next changes status.

The trash offers restoration and no deletion, because deleting from there would mean purging, which the retention window owns rather than the user.

## Retrying a conflicted batch without asking

A batch that fails on a stale version is retried once, silently, for status changes only, using versions read through the selection endpoint. This loosens the rule that a conflict always returns to the user, and it is safe for exactly this family: a status change is idempotent server-side, already-satisfied items are no-ops that echo their version unchanged, and the domain guards reject the transitions that would be wrong, so a retry either converges on the user's intent or fails loudly with the real reason. The retry commits only if the store still matches the state it was read from, so it can never write against state nobody saw.

Deletion is never retried automatically. It is the one batch whose intent can invert while the user is deciding — a TODO archived as junk may have been reopened elsewhere — so it always returns to a person. Its confirmation dialog reads the selection's current state on open and confirms with the versions it displayed, which shortens the window between deciding and acting to seconds without pretending it can be closed.

A retry runs only when every selected identifier is still resolvable. A shrunken selection would act on a subset the user never chose, so any absence goes back to the user instead.

## Two conflict policies, governed by invariants rather than lockstep

The assistant carries its own copy of the rule above, in `BulkConflictPolicy.cs`, alongside the browser's in `useBulkActions.ts`. The copy is deliberate. The policy cannot move into the handlers, because those are shared with the HTTP path: a retry inside one would make the browser's writes silently retry too, which is precisely the behaviour the single-item routes refuse.

Each copy independently holds three invariants: a retry applies to status changes only, never to deletion or restoration; there is at most one retry; and a retry proceeds only when every selected identifier still resolves. These derive from the domain — intent inversion on delete, the all-or-nothing batch — not from the other copy, so neither has to watch the other to stay correct. No divergence between them can corrupt data, because the server arbitrates every interleaving through the version check, and no actor observes both policies on one operation.

What looked like duplication mostly is not. Classification is single-sourced already: the browser reads the server's problem title, and the assistant catches the server's own exception types. Representation is necessarily different — the browser highlights drifted cards and diffs a live selection inside a dialog, and the server cannot see a rendered screen.

The only coherent route to one policy is a product decision to make the assistant the sole bulk write path. Consolidating them as a refactor is not that decision, and would trade a stated invariant for a shared abstraction that has to serve two actors with different repair surfaces.

## The assistant acts as the user, in the user's own request

Assistant turns dispatch MediatR commands inside the caller's authenticated HTTP request, synchronously and in process. `ICurrentUser` resolves from that HTTP context, so the assistant acts *as* the user by construction: there is no impersonation, no machine credential, and no second authorization surface to keep aligned with the first.

Tools send commands and queries only, never repositories. Every call therefore inherits `ValidationBehavior`, `DomainRuleExceptionBehavior`, `RequestLoggingBehavior`, and the ownership scoping in the persistence boundary — every guardrail the HTTP API has. There is no path by which the assistant can do something a browser could not.

Assistant-issued commands open a logging scope carrying `RequestOrigin`, so a log answers "did I do that or did the assistant?" without the request pipeline needing to know the assistant exists.

The threat model this leaves is small and stated: a TODO's name and description are the user's own text, and the system prompt says to read them as data even when they are phrased as instructions. The blast radius is already bounded by owner scoping and by the confirmation gate, so prompt injection through a TODO can at worst propose something to the user's own list that the user is then asked to confirm.

## Version binding: reads return versions, writes take identifiers

Assistant read tools return versions; assistant write tools accept identifiers only, and the tool layer binds each one to the version the model last read it at. This mirrors the browser exactly — "version sent" equals "version the actor last saw" — and it is the reason writes are safe without trusting the model.

The two rejected alternatives are worth naming. Letting the model supply versions would put an inventable value on the concurrency check. Reading a version immediately before writing would be a blind overwrite wearing an optimistic check, since nothing would have observed the state in between.

The ledger is seeded from the echoed transcript at the start of each turn, by scanning it for objects carrying both an identifier and a version. Without that, a conversation that read its TODOs three turns ago could never write to them: the server keeps no history, and the model will not re-read something it can still see in its own context.

Deletion binds differently, and for the same reason the browser's dialog does. The proposal reads the selection's current state, displays it, and the confirming turn executes with exactly those versions. Replay safety falls out of that: a repeated confirmation carries a version the store has already moved past.

## Provider neutrality and bring-your-own-key

The loop is `Microsoft.Extensions.AI`'s `IChatClient`, with tools defined once as `AIFunction`s. Two adapters ship: Anthropic through its own SDK's adapter, and an OpenAI-compatible client whose base URL is configurable, which reaches OpenRouter, Ollama, vLLM, LM Studio, and most self-hosted setups without a provider type for each.

Provider flexibility is safe here because correctness never depended on the model. It only *proposes* tool calls; the version binding and the domain guards decide. A weaker model mis-calls tools more often, and a malformed or over-cap call fails validation and returns as an honest tool error the model can react to. What degrades is helpfulness, never correctness.

Keys are the user's own, which dissolves the application's cost concern. They are encrypted with ASP.NET Data Protection before they reach persistence and decrypted only where a provider client is built, so the repository stores a string it cannot read and nothing on that path can log a usable secret. The API surface is write-only: a key can be replaced but never retrieved, so a stolen session cannot be used to walk away with the credential. An application-level key remains as an optional fallback, and a connection always resolves wholly from one source — falling back key-only would pair the application's credential with the user's chosen model, which is wrong whenever the two name different providers.

Prompt-order hygiene is provider-neutral rather than Anthropic-specific: the tool set is identical on every request, and dynamic content such as the date lives in the conversation's first user message rather than the system prompt. A per-request tool set or a timestamped system prompt moves the cacheable prefix and defeats caching wherever a provider offers it. Anthropic's `max_tokens` is sized for thinking plus response text, because thinking is on by default on current models and shares that cap; other providers keep their own defaults.

## Streaming a turn over POST, with the transcript held by the client

A turn is watched while it happens, so it streams as server-sent events. The client parses them off the `fetch` body rather than using `EventSource`, because antiforgery is a global requirement here and `EventSource` can only issue a GET. Events are coarse, and there are no reconnection semantics: a dropped stream loses nothing, because a tool call that committed stays committed.

Conversation state is held by the client and echoed each turn, including tool-call and result content. The server therefore stores no history and needs no schema for one. Tampering gains nothing — the assistant runs with exactly the caller's rights and dispatches commands the caller can already send over HTTP — so a mangled transcript starts a fresh conversation rather than failing the turn. A server-side store is a history *feature* for later, not a correctness need.

Two events beyond the coarse set exist for mechanical reasons. `heartbeat` keeps an idle stream, and any proxy in front of it, from timing out while the model thinks. `turn_completed` carries the transcript forward; without it a stateless server would leave the next turn with nothing to continue from.

## Windowing the replayed conversation rather than storing it

The client holds the transcript and echoes it back each turn, which is what lets
the server keep no history. Nothing bounded it: request bodies grew with the
conversation, and every later turn replayed the whole thing to the provider, so
its cost grew with it and a long conversation would eventually exceed the
model's context window outright.

The bound is applied where the conversation is replayed. The opening message
survives — it carries the date and is the still prefix prompt caching depends on
— and the most recent `Assistant:TranscriptMaxMessages` follow it. Because the
windowed conversation is also what the turn hands back, the copy the client
holds stops growing as well, so one bound fixes the wire, the context, and the
token cost together. Nothing the person sees changes: the client renders its
chat log from turn events as they arrive and treats the transcript as opaque.

The ledger is seeded from the windowed conversation rather than from what
arrived. Seeding from the full transcript would leave versions bound to reads
the model can no longer see, which is the blind overwrite the version-binding
rule exists to prevent; dropping them together means an unread identifier
returns the same "read it first" a fresh conversation gets. A window that would
open on a tool result advances past it instead, because a result whose call was
trimmed away is an orphan providers reject.

Server-side conversation storage was not chosen instead. It fixes the wire and
enables a history feature, but not the token cost — history still has to be
replayed — so the window is the part that would survive it. It remains a history
*feature* for later rather than a correctness need.

The endpoint carries a request size limit as a backstop. A client that echoes
what it was handed stays far below it; what it catches is one that does not,
before the host's own multi-megabyte default would.

## Refusing an over-cap batch rather than splitting it

The assistant declares the hundred-item batch cap in its tool schemas, sourced from `BulkTodoLimits`, so a model never composes a batch that was doomed before it was sent. Asked for more, it narrows and asks which ones the user means.

Chunking was rejected. Splitting a batch abandons the all-or-nothing guarantee the bulk endpoints exist to provide, and leaves the assistant unable to describe honestly what actually happened — which is the one thing it must be able to do.

## API failure contract

The API uses one global exception handler rather than controller-level
try/catch blocks. Application not-found, concurrency, and domain-rule
exceptions map to stable RFC Problem Details responses. FluentValidation and
ASP.NET Core model-binding failures share the same 400 title, detail, trace ID,
and field-error shape.

## Provider-neutral structured logging

Serilog is the API host's logging provider, but application and infrastructure
code depend only on Microsoft `ILogger<T>`. Typed loggers provide automatic
source categories and allow per-category filtering without coupling use cases
or persistence code to Serilog. Static Serilog access is restricted to bootstrap,
fatal startup reporting, and shutdown flushing in `Program.cs`.

The logging pipeline produces a condensed HTTP completion event, trace context,
MediatR request timing, index-initialization events, and a post-commit event when
a recurring completion creates its next TODO. Successful TODO mutations emit
Information audit events through direct typed `ILogger<T>` calls with stable
event IDs and structured placeholders. Logging records identifiers, versions,
and operational metadata, not request bodies, descriptions, cursor values, or
connection strings. Successful health probes are Debug events, while unhealthy
probes remain Warning events. Known 400, 404, and 409 outcomes remain normal
request events. Unexpected exceptions are logged once at Error with the trace
ID, method, and path by the global exception handler; the corresponding HTTP
completion remains a Warning to avoid a second error event for the same failure.

## Fail-fast injected dependencies

Required constructor-injected services are guarded with
`ArgumentNullException.ThrowIfNull`. Although the runtime container normally
guarantees required registrations, explicit guards provide deterministic
failures for direct construction, tests, factories, and future registration
changes. Optional parameters are not converted into required dependencies; the
repository's optional transaction context remains an intentional fallback for
non-transactional direct construction.

## Deterministic TODO list pagination

The list read path uses a dedicated `ITodoListReader` abstraction rather than
expanding the mutation-oriented aggregate repository. Infrastructure implements
the reader as a MongoDB aggregation so dependency documents can be joined and
blocked state can be calculated before a blocked/unblocked filter, cursor, or
limit is applied.

Cursors are versioned JSON payloads encoded with Base64URL. They bind the last
sort value and TODO ID to the selected sort, direction, scope, and filter
signature. Reusing a cursor after any bound option changes is a 400 error. Every
sort uses the TODO ID in the same direction as its final tie-breaker, and the
reader fetches one item beyond the requested limit to decide whether to return
another cursor.

Priority ordering is Low, Medium, High; status ordering is NotStarted,
InProgress, Completed, Archived. These are explicit business orders and do not
depend on the alphabetical BSON representation.

## Fluent list aggregation

The list reader uses typed MongoDB filter, cursor, sort, and limit builders for
the ordinary query path. Raw BSON is retained only for aggregation expressions
where it makes the lookup, count, and final computed projection explicit. The
reader projects into an internal list-row model before mapping to the public
DTO, keeping persistence details out of Application.

Dependency-state filtering runs before pagination; ordinary requests sort and
limit before the dependency lookup to bound work. Both paths preserve the same
deterministic cursor contract and use `_id` as the final tie-breaker.

## Read-time dependency state

`incompleteDependencyCount` and `isBlocked` are calculated during list reads
rather than persisted. Persisting them would create stale denormalized state
whenever a dependency is completed, deleted, archived, or removed. The lookup
counts only active completed dependency IDs, so missing, deleted, archived, and
unfinished dependencies remain incomplete and the list behavior matches status
transition rules.

## Local replica set

Local development uses one MongoDB 7.0 replica-set member. The Compose
initializer is idempotent: it checks replica-set status and initiates `rs0` only
for an uninitialized database. The member advertises `localhost:27017` so the
host-run API can use the committed connection string. This single-member setup
supports optimistic writes and transactional recurring completion.

## Recurrence and atomic completion

Recurrence is represented by a domain value object containing a schedule type,
positive interval, unit, and monthly anchor where relevant. Standard daily,
weekly, and monthly schedules use an interval of one; custom schedules support
every N days, weeks, or months. Calculations start from the scheduled due date,
not completion time, so late completion does not cause schedule drift. Monthly
calculation reconstructs the target day from the stored anchor, preserving
end-of-month and leap-year behavior.

The first recurring TODO receives a series ID and occurrence number 1. A real
transition into `Completed` raises `TodoCompletedDomainEvent`; a no-op
Completed-to-Completed request raises nothing. Application dispatches the event
in-process while the MongoDB session transaction is active. Its handler inserts
the next occurrence with copied name, description, priority, recurrence, and
series data, but no dependencies. A handler failure aborts the completed update.

The transaction reuses the existing expected-version filter. A unique partial
index on `seriesId + occurrenceNumber` is the second idempotency boundary.
Concurrent completion therefore produces one committed completion and next
occurrence, while the stale request returns 409.

## Cookie session with OIDC login

Authentication is designed as OpenID Connect for login and an encrypted
ASP.NET Core cookie for the application session. Until that slice is
implemented the API still has no authentication and must not be exposed
publicly.

Browser-held tokens were rejected. Any access or refresh token reachable from
JavaScript is exposed to cross-site scripting, and client-side refresh adds
rotation and storage concerns that a server-side session already solves. The
React client therefore receives no access, ID, or refresh token, and no gateway
or token-relay tier is introduced.

The cookie handler is registered as the default challenge scheme and OpenID
Connect is challenged only by the dedicated login endpoint. This is recorded
because the intuitive configuration produces the opposite behavior: with
OpenID Connect as the default challenge, an unauthenticated `fetch` receives a
redirect to the provider rather than `401`, and the failure surfaces in the
client as an opaque cross-origin navigation instead of a status code the client
can act on.

Sessions expire after eight hours and slide on use. Production deployments must
persist Data Protection keys so sessions survive restarts and stay valid across
API instances.

The key ring is the one piece of state whose loss is not recoverable by
restarting. It also encrypts stored provider API keys, which the API will not
return to a caller, so a lost ring cannot be repaired by reading the old value
back — each user has to enter theirs again. The image therefore writes the ring
to `/keys` and creates that directory owned by the user it runs as, so a
deployment persists it by mounting a volume rather than by also remembering a
setting. The ring and the database are one backup unit: restoring MongoDB
against a different ring yields provider keys nobody can decrypt. Keys roll
roughly every 90 days and the superseded ones are retained deliberately, since
they are what reads anything encrypted before the roll.

## Internal user identity separate from the OIDC subject

A user document maps the provider-owned `issuer` and `subject` pair to an
internal `Guid` user ID, with a unique index on that pair. The OIDC subject
remains a string, consistent with the identifier policy above, while ownership
and indexing use the same binary UUID representation as every other
backend-owned identifier.

The indirection keeps provider identifiers out of TODO documents. Changing
identity provider, or running more than one, becomes a mapping change rather
than a data migration of every owned record.

First login inserts the mapping, so two concurrent callbacks for a new user can
race. The unique index is the authority: the losing insert catches the duplicate
key error and re-reads the winner rather than failing the login.

## Ownership enforced in the persistence boundary

Each TODO stores an `OwnerId`, and the owner predicate is applied inside the
shared repository and list-reader filters rather than added by each handler.
Threading an owner argument through every query was rejected: it makes every
current and future call site a place where the filter can be omitted, and an
omission is a cross-tenant data leak rather than a visible bug. Handlers cannot
forget a filter they never supply.

The repository refuses to operate without an authenticated user. The retention
purge path is the deliberate exception, since it is maintenance work that spans
owners.

Requests for another user's TODO return `404` rather than `403`, so the response
does not confirm that the identifier exists.

Sort and lookup indexes take `ownerId` as their leading key because every query
now filters on it first. The index initializer only creates indexes, so the
superseded index names are dropped explicitly before creation; otherwise an
existing deployment keeps unused indexes that still cost write time. Existing
TODO documents predate `OwnerId` and cannot be attributed to a user, so
disposable local data is recreated rather than backfilled.

## Application-only logout

Logout deletes the application cookie and returns `204`. The provider's
end-session endpoint is deliberately not called.

Provider logout requires an `id_token_hint`, which would mean persisting the ID
token in the authentication ticket purely to support sign-out, and a `fetch`
cannot follow a redirect into a provider logout page regardless. The accepted
trade-off is that the provider session can outlive the application session, so a
login immediately after logout may complete without a new credential prompt.
True single logout is a later decision if shared-device use is ever required.

## Antiforgery as a global requirement

Cookie authentication reintroduces cross-site request forgery, so state-changing
requests carry an antiforgery token in a request header, validated by a global
filter. Per-endpoint attributes were rejected for the same reason as per-handler
ownership filters: an unprotected mutation should require a deliberate opt-out
rather than a remembered opt-in.

Antiforgery tokens are bound to the authenticated identity, so a token obtained
before login fails validation afterwards. The client refreshes its token on
startup, after login, and after logout. The antiforgery cookie may be readable
by JavaScript; it is not an authentication credential, and the session cookie
remains HttpOnly.

## Local identity provider for development and browser tests

Backend integration tests use a Testing-only authentication handler that stamps
a configurable user ID claim. It exists only in the test host and is never a
production bypass. Because every existing API test becomes unauthenticated the
moment the controller requires authorization, this handler is built first in the
slice rather than last.

Playwright drives the real stack, so a test-only handler is not sufficient
there. Development and browser tests run against a local provider in Compose
with seeded users, which also keeps the two-user isolation test honest. The
production provider then differs only by configuration.

## SCSS rather than the indented Sass syntax

Client styles are authored in SCSS. The indented `.sass` syntax was preferred on
readability grounds and rejected on tooling grounds.

The styling rules this repository intends to enforce — design tokens instead of
literal colours, a fixed property order, shared mixins in place of repeated
typography and truncation declarations, and no unused `@use` — are only worth
writing down if a check can fail on them. Enforcing them requires a PostCSS
parser that stylelint can drive, and no such parser handles the indented syntax.
`postcss-sass` parses it but discards every Sass at-rule: a file whose `@mixin`
body contains a literal colour and an `!important` yields an empty syntax tree
and zero reported problems. The mixin and import rules would be unenforceable,
and a mixin would become the one place a violation could hide. It was also last
released in 2022. `sass-parser`, the Sass team's own PostCSS wrapper, does read
the indented syntax correctly including mixin bodies, but is pre-1.0 and fails
as a stylelint custom syntax in two separate places, so it cannot serve as a
gate today.

SCSS is parsed by `postcss-scss`, which the stylelint project maintains, and is
the syntax `stylelint-scss` targets. Choosing it turns each rule above into a
build failure rather than a convention. The accepted trade-off is braces and
semicolons in exchange for enforcement.

This is a tooling decision, not a language one. The token and mixin structure is
independent of syntax and would convert mechanically, so `sass-parser` reaching
1.0 with a working stylelint custom syntax is the trigger to revisit it.

## Scoped class names through CSS Modules

Component styles are CSS Modules named `*.module.scss`. Only `index.scss`
remains global, and it holds document-level concerns: the `:root` palette and
typeface, the `body` background, the heading resets, and the form-element colour
inheritance.

The stylesheet this client grew declared `.button`, `.status`, `.version`,
`.blocked`, `.muted`, and `.priority` in a single global namespace. Names that
generic are a collision waiting for the second component that wants a status
chip, and the collision would be silent. The failure runs in the other direction
too: the error banner rendered `error-${kind}`, producing `error-network` and
`error-concurrency` classes that no rule ever defined, and nothing reported the
dead reference. Under modules that class is a missing export rather than a
string that quietly resolves to nothing, so the error kind is now a
`data-error-kind` attribute, which is what it was always being used as.

React has no styling system of its own, so the choice was only ever which CSS
mechanism to use. Inline `style` was not a candidate: it cannot express the
`:focus`, `:disabled`, `[aria-selected]`, and `@media` rules this client
depends on. CSS Modules keeps the token and mixin layer intact, needs no runtime
dependency, and Vite compiles it without additional configuration.

`css.modules.localsConvention` is set to `camelCaseOnly` so stylesheets keep
writing kebab-case class names, which is what the naming rule in
docs/coding-standards.md enforces, while components read them as
`styles.todoCard`.

Sharing between modules happens through the mixins in `styles/_mixins.scss`
rather than by importing another component's module. A module that imports a
sibling's stylesheet reintroduces the coupling scoping was meant to remove, so
`surface`, `field`, `focus-ring`, and `action-row` exist as mixins and each
module names its own class.

One consequence is that Playwright can no longer select a hashed class name.
`todo-crud.spec.ts` located a TODO's identifier through `.todo-id`; that element
now carries `data-testid="record-id"`.

The name deliberately avoids a `todo-` prefix. The suite identifies a card with
`[data-testid^="todo-"]`, so a `todo-id` test hook inside each card matched that
prefix as well and doubled every count the suite took.

## Type-checked CSS module class names

Vite types every `*.module.scss` through a single wildcard declaration whose
members are an index signature. Under it `styles.todoCrad` compiles, renders
`class=""`, and the element loses its styling with nothing reported at build
time or in the browser. Scoping the class names removed the collision risk but
left this failure untouched.

A declaration generated beside each module resolves ahead of that wildcard and
turns the same typo into `Property 'todoCradHeading' does not exist. Did you
mean 'todoCardHeading'?`. TypeScript finds the files through
`allowArbitraryExtensions`, which the project already enabled: an import of
`./X.module.scss` resolves to `./X.module.d.scss.ts`.

`scripts/generate-css-module-types.mjs` compiles each module with Sass before
reading its classes rather than scanning the source, so a class that only a
mixin or a nested block introduces is declared like any other. Names are
camel-cased to match the `camelCaseOnly` setting in vite.config.ts, which is the
only spelling a component can use.

The declarations are generated, so they are git-ignored rather than committed,
which keeps them from drifting from the stylesheets they describe. `yarn dev`
and `yarn build` write them before anything reads them, Playwright inherits that
through `yarn dev`, and CI runs an explicit step because the type-aware lint
runs before the build.

## The successor if CSS Modules is outgrown

styled-components is not the fallback. It is in maintenance mode, and a runtime
CSS-in-JS library requires every styled component to be a client component,
which is why the ecosystem moved away from it. The cost is concrete here as
well: stylelint cannot meaningfully parse styles inside tagged template
literals, so adopting it would discard the property-order, token, typography,
and truncation rules recorded above.

If type-safe tokens expressed in TypeScript become a real requirement, the
destination is a zero-runtime CSS-in-TS system such as vanilla-extract or Panda
CSS. Both provide typed tokens and co-location without a runtime and without the
server-component problem.

Staying put is the cheaper bet because the migration cost is asymmetric. Design
decisions are centralised in `styles/_tokens.scss`, which converts to a
TypeScript object mechanically, and the mixins convert to functions. The reverse
is not mechanical: styles interpolated with component logic have to be read by
hand, and the lint layer that could inventory them is exactly what was given up.

Nothing in the client presently needs a style value computed at runtime. Every
variant is a finite set resolved to a class — eight badge tones, four button
variants — and the remaining behaviour is `:focus`, `:disabled`,
`[aria-selected]`, and one media query. An open-ended value, if one arrives, is
a CSS custom property set from an inline style, which keeps the token and mixin
layers intact.

## Deferred decisions

Retention cleanup scheduling remains deferred until its vertical slice.
Selecting the production identity provider is the remaining authentication
decision and is now a configuration choice rather than a design one.
