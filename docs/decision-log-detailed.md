# Decision Log

This document records architecture decisions, requirement interpretations,
trade-offs, known limitations, and intentionally omitted features.

## Layered monolith

The backend is a layered monolith split into Domain, Application,
Infrastructure, API, and Assistant projects. This keeps business rules
independent of HTTP, MongoDB, and any model provider without introducing
distributed-system overhead. Dependencies point inward: API composes
Application, Infrastructure, and Assistant; Infrastructure implements
Application abstractions; Assistant references Application alone and is the
only project that references a provider SDK; and Domain has no outward
dependencies.

The React client is a separate build artifact but consumes the same HTTP API.
Additional deployable backend services should only be introduced when an
independent scaling or ownership requirement justifies them.

## CQRS with MediatR

HTTP controllers — and the assistant's tools — send commands and queries
through MediatR. Each request has a single handler, and cross-cutting request
logging, validation, Space access, and domain-exception translation run as
pipeline behaviors. Commands and queries share one MongoDB data model;
CQRS here separates use-case code rather than introducing separate read and
write databases.

This keeps controllers thin and handlers independently testable while avoiding
an event bus or messaging infrastructure for an in-process application. MediatR
carries requests only. Nothing here is event-driven: there is no bus, no
pub/sub, and no eventual consistency, and the one thing that started out as a
domain event no longer is — see "Recurrence and atomic completion".

Commands, queries, and the DTOs they return are records. They are immutable
data with no identity of their own, so value equality and a readable
`ToString` are what a test and a log want from them. Types that carry identity
or behaviour stay classes — the `TodoItem` aggregate above all, whose equality
is its `Id` and whose whole purpose is to change state under rules. API request
contracts also stay classes, because model binding and their validation
attributes are the reason they exist.

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

The dependency selector originally loaded at most 100 active TODO projections
ordered by name and searched within that bounded set, which kept browser memory
and network work bounded without a new API contract but left every candidate
beyond that first page unreachable. That follow-up has since been taken: the
selector sends what is typed to the list endpoint and the server matches it, so
the reachable set is the whole collection rather than one page. Only the
predicates the server cannot know stay in the client — the card excludes itself
and the prerequisites it already has. The matching semantics are recorded under
"Token-prefix search over stored search tokens".

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

Application code depends on `ITodoRepository`, while Infrastructure provides `TodoRepository`. The repository uses `IMongoDatabase` directly rather than introducing a thin `MongoDbContext` wrapper. MongoDB documents and mapping types remain internal so persistence-specific BSON concerns cannot leak into Application or Domain code.

Integration tests use the public repository contract. Exact storage representations are checked as raw BSON after writing through the repository, avoiding both `InternalsVisibleTo` and a public persistence document type.

## Persisted enum representation

`TodoStatus` and `TodoPriority` are persisted as BSON `int32` values because
their explicit numeric values represent the required business ordering. This
lets MongoDB use the existing sort indexes directly and removes the temporary
rank expressions previously needed by list queries.

Integer storage is the only representation the application reads or writes; a
startup migrator that converted legacy string values was removed once it had no
data left to convert. Nothing has been deployed anywhere its data outlives a
schema change, so a database written by an older build is recreated rather than
migrated. Reintroducing a conversion step is the cost of the first deployment
target that owns data worth keeping.

## Optimistic concurrency

The numeric `version` field is the sole concurrency token; `updatedAt` is not used for concurrency. Clients send the version they last read with every mutable request.

MongoDB mutations atomically filter by TODO ID and expected version, write version `N + 1`, and return the persisted document. A missing match indicates a stale write, which the repository reports by throwing `ConcurrencyConflictException`. The expected version is the aggregate's own `version`; no domain method mutates it, so callers do not pass it alongside the aggregate.

This design avoids locks and prevents lost updates. Concurrent integration tests verify that exactly one mutation succeeds when two writers submit update, delete, or restore operations using the same version.

## Transaction boundary

Operations that must write more than one document run through `ITransactionExecutor.ExecuteAsync`, which takes the work as a lambda, commits when it returns, and aborts when it throws. Scoping the boundary this way means no caller can forget to commit or roll back, and repository calls made inside the operation join the transaction through the scoped session context.

The name is deliberate: this is not a unit of work. Nothing is tracked, deferred, or coordinated, and repository writes stay immediate, so responses are read back from the server rather than predicted. Calling it `IUnitOfWork` would promise the change tracking and `SaveChanges` semantics the design does not have.

Conflicts are reported where their detail is known. The repository throws `ConcurrencyConflictException` carrying the TODO ID and expected version, because it issued the filter. The executor throws `TransactionConflictException` without an identifier, because an aborted transaction does not say which document lost. The API maps both to 409, which always means "re-run the read-modify-write" — deliberately including transient cluster events such as a primary stepdown, which are surfaced to the client rather than retried server-side.

Schema bootstrap is excluded from this boundary. MongoDB forbids index creation and removal inside a transaction, hosted services run before the request pipeline exists, and the index initializer is idempotent, so there is nothing to roll back.

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

Archiving is treated as putting a TODO beyond further work rather than as one status among equals. An archived TODO rejects edits, dependency changes, and completion; only unarchiving to `Open` or `InProgress` reopens it. Soft delete stays available so archived records can still be cleaned up, and archiving itself remains reachable from every other status.

The rule lives in the domain, so the single-item and bulk endpoints inherit identical behaviour rather than agreeing by convention. It tightens `PUT /{id}`, `PUT /{id}/status` towards `Completed`, and dependency changes on archived TODOs into 409 responses.

## Bulk actions mirror the single-item API

Bulk endpoints reuse the vocabulary of the single-item routes — `PUT /api/spaces/{spaceId}/todos/status` and `DELETE /api/spaces/{spaceId}/todos` — rather than introducing an action name and a discriminator enum. Widening bulk status changes to reopening or unarchiving later becomes a validator change instead of a new endpoint. Literal segments outrank route parameters, and the `{id}` routes carry `:guid` constraints so that precedence is enforced rather than incidental.

A batch is all-or-nothing. Every selected TODO is loaded in one query, a missing identifier fails as 404 before any version is compared, and every stale version is reported together. Validation runs against the whole selection, so completing a prerequisite alongside its dependent succeeds while completing a TODO blocked by something outside the selection fails the entire request. Partial success was rejected because dependency chains and recurring occurrence creation would leave callers unable to tell what actually happened.

Both delete routes answer 200 with a body, because the response carries the new version and deletion timestamp that a following restore needs. The single-item route returned 204 until the client began to need that version.

## Reading a selection without disturbing the list

`GET /api/spaces/{spaceId}/todos/selection` reports the current state of specific identifiers. A client holding a selection that the server has just rejected needs to know what changed and what vanished, and it needs that without refreshing the list, because a selection resolves its versions from what is on screen: refreshing underneath one would send versions the user never saw.

The list route cannot answer this. Its scope defaults to Active, so a TODO that drifted into Archived reads as vanished; its cursor is bound to a filter signature that an identifier set does not have; and covering a selection by paging is unsound at any page size.

The read is found-only. Identifiers that no longer resolve are absent from the response rather than failing it, which is the opposite of the batch loader, where a missing identifier fails the whole request as 404. The two differ because their jobs differ: a write must refuse to act on a selection it cannot fully honour, while a probe exists precisely to report what is no longer there.

Soft-deleted TODOs still resolve. The trash lists them and a selection there is restorable, so hiding them would leave a conflict in that scope permanently unrepairable — the probe would report every selected TODO as vanished. Only what is purged, or outside the bound Space, is absent. `deletedAt` on each item tells the caller which state it is in.

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

Assistant turns dispatch MediatR commands inside the caller's authenticated HTTP request, synchronously and in process. `ICurrentUser` resolves from that HTTP context, so the assistant acts *as* the user by construction: there is no impersonation and no machine credential.

There is a second authorization surface, and it is deliberate. A turn names the Space it runs in, and that Space is checked before the turn starts rather than left to the commands the tools will eventually send. Two facts about a turn force the check forward. It answers as server-sent events, so once the stream has opened a failure can no longer become a status code — the status line is already written — and a refusal has to be decided before the first byte if it is to arrive as an ordinary `404` or `403`. And a confirmed deletion is applied at the top of the run, ahead of any call to the model, so a turn nobody authorized must not reach that point either. The check therefore runs twice: in the controller, where it gives a refusal its HTTP shape, and as the runner's first statement, where it binds the ambient Space scope the tools then dispatch under.

What keeps that from becoming a second rule to hold in step with the first is that both call `ISpaceAccessService` — the same service `SpaceAccessBehavior` calls. One rule, three call sites, no re-implementation of who may do what.

Tools send commands and queries only, never repositories. Every call therefore inherits `ValidationBehavior`, `DomainRuleExceptionBehavior`, `RequestLoggingBehavior`, and the Space scoping in the persistence boundary — every guardrail the HTTP API has. There is no path by which the assistant can do something a browser could not. The tool schemas the model sees carry no Space: the Space comes from the turn, is fixed for the turn's duration, and is not a parameter the model can name, so it cannot be argued into acting somewhere else. A Read member's toolset is identical too; the writes fail through the handlers, and a sentence in the context tells the model what it will find.

Assistant-issued commands open a logging scope carrying `RequestOrigin`, so a log answers "did I do that or did the assistant?" without the request pipeline needing to know the assistant exists.

The threat model this leaves is small and stated: a TODO's name and description are text the members of a Space wrote, and the system prompt says to read them as data even when they are phrased as instructions. The blast radius is bounded by the Space scope and by the confirmation gate, so prompt injection through a TODO can at worst propose something inside a Space the reader is already a member of, and the reader is then asked to confirm it. Sharing widens who can plant that text — a Write member can — without widening what it can reach.

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
MediatR request timing, index-initialization events, and a
post-commit event when a recurring completion creates its next TODO. Successful TODO mutations emit
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

Priority ordering is Low, Medium, High; status ordering is Open,
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

Local development uses one MongoDB 8.0 replica-set member. The Compose
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
transition into `Completed` records a `TodoCompletion` on the aggregate; a
no-op Completed-to-Completed request records nothing. The status handler reads
that property and, when it carries a recurrence, inserts the next occurrence
with copied name, description, priority, recurrence, and series data, but no
dependencies, inside the MongoDB session transaction. A failed insert aborts
the completed update. A reopened occurrence completed a second time inserts
nothing when the series already holds the next position — its successor was
created the first time round and may since have been worked on — and writes
only the completion.

This began as a domain event behind a hand-written dispatcher, and a MediatR
notification was tried in its place; both were removed, and then so was the
event vocabulary. With one type, one place raising it, and one consumer, the
indirection only obscured that the successor is written in the same
transaction, and it left two ways of creating a next occurrence, because the
bulk path never dispatched at all — it read the event as data and called
`IRecurringOccurrenceFactory` directly. The single-item path now does the same,
so the two agree by construction rather than by review.

What survives is the job the event was always doing underneath: reporting out
of the aggregate. `ChangeStatus` returns `bool`, but a completion decides more
than "something changed" — it mints the successor's identifier so the insert
and the response agree on it before the write. A single nullable property
carries that, in place of a collection, a type filter, and a `ClearDomainEvents`
call that existed to stop a second dispatch. The name follows the mechanism:
nothing subscribes, so calling it an event promised a subscriber that was never
going to arrive.

The property is assigned on every transition rather than only on a completion,
because it is now state rather than a queue something drains. Reopening a
completed TODO clears it; otherwise a stale completion would survive and
produce a second successor.

Removing dispatch also settled the question of a mediator in Domain. There is
no marker interface left, and Domain has no package references — this time
because nothing needs one, rather than as a rule held for its own sake.

The transaction reuses the existing expected-version filter. A unique partial
index on `spaceId + seriesId + occurrenceNumber` is the second idempotency
boundary.
Concurrent completion therefore produces one committed completion and next
occurrence, while the stale request returns 409.

## Cookie session with OIDC login

Login is OpenID Connect and the application session is an encrypted ASP.NET
Core cookie. Every TODO route requires an authenticated user, and a request
without a valid session is answered with `401` rather than a redirect.

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

## The Space aggregate: an embedded, versioned access list

A Space is an identifier, a name, an access list, a version, and timestamps, and
the access list is embedded in the Space document rather than kept as rows in a
membership collection. Reads decide this. Every Space-scoped request begins by
loading one Space and asking what the caller may do there, which as an embedded
list is a single read against the primary key; membership rows make it a query,
plus a second read for the name the interface shows beside the answer. The cost
lands on the write side: adding a member rewrites the Space, so two Owners
editing the access list at the same moment contend where independent rows would
not. That is what the version is there to arbitrate, and editing membership is
rare next to reading it.

Name and access list are versioned together because they move together — same
screen, same permission, and a caller who read one read both — so one version
covers the whole document a client last saw.

`Space.Version` deliberately does not move when a TODO in the Space is written.
Bumping it would turn every create, edit, and status change into a write to one
shared document: a Space-wide lock, which is exactly what this design avoids,
and which would make concurrency a property of the list rather than of the item
being changed. The Space's version therefore answers "did the name or the
membership move under me?", the TODO's version answers "did this item move under
me?", and neither question can be spoiled by the other's answer.

The personal Space's identifier is derived from the user rather than generated:
a UUID v5 over a fixed namespace and the internal user ID. It is created lazily,
on the user's first listing, by an insert that treats a duplicate key as success
and re-reads the winner. A check-then-create would race — two first requests can
both find nothing and both insert — and a derived identifier removes the race
instead of coordinating it, the same way the user directory's unique index
settles two concurrent first logins. Nothing marks the result as special: "My
Space" is renameable, shareable, and in every other respect an ordinary Space,
so no other code has to know that a second kind of Space exists.

## Space access enforced by the pipeline, not at each call site

Each TODO stores the `SpaceId` of the Space that contains it, and a
`CreatedByUserId` that is audit data and is never consulted for authorization.
Access is a property of the Space rather than of the TODO: the Space holds the
access list, and a member's level in it decides what they may do to everything
inside.

This entry replaces an earlier one that put ownership in the persistence
boundary, and the part of that decision worth keeping is its rejection of
threading an owner argument through every query — because it makes every current
and future call site a place where the filter can be omitted, and an omission is
a data leak rather than a visible bug. That conclusion stands and no Space
argument is threaded through any repository or reader signature either. The
mechanism behind it is what moved, from one filter applied in one place to a
pair:

- **`SpaceAccessBehavior`**, a MediatR behaviour running after validation. A
  request implementing `ISpaceScopedRequest` declares the Space it acts in and
  the level it needs; the behaviour resolves the caller's entry in that Space
  and answers before any handler runs. Handlers hold no `Require` calls.
- **`ISpaceScope`**, request-scoped, bound only by a check that passed.
  `TodoRepository` and `MongoTodoListReader` read it exactly the way they read
  `ICurrentUser`, inside the same shared identifier, mutation, and list filters,
  so reads, existence checks, batch loads, dependency lookups, graph traversal,
  active-dependent checks, mutations, and cursor pages stay scoped by
  construction.

The scope is fail-closed, which is what makes it a guarantee rather than a
convenience. Reading a Space from an unbound scope throws, so a query that
reached persistence without an authorization step fails loudly instead of
quietly matching every Space in the collection — the property the old rule got
from a repository that refused to run without an authenticated user. Binding
twice to different Spaces within one request is refused for the same reason: it
would mean work authorized for one Space was dispatched in another.

The repository's own `spaceId ==` filter is not redundant with the behaviour but
the second wall behind it. The behaviour decides whether this caller may act in
this Space; the filter decides which documents that Space contains. Neither
alone failing lets a request cross a boundary.

The two refusals are deliberately different answers. A caller who is not a
member is told `404` for the Space and for everything under it, so a probe
cannot separate an identifier that does not exist from one that belongs to
someone else. A member below the level a route needs is told `403`, which does
confirm the Space exists — they are in it, so they already knew — and names the
real problem instead of hiding it. The client has to keep the two apart from
`401`: an expired session ends the session, a refused permission does not.

The gap between the check and the write is accepted rather than closed. The
behaviour reads the access list, the handler writes moments later, and a member
removed in between still lands that write. Closing it needs a lock over the
Space or a transaction spanning the access list and the TODO, paid by every
writer to correct a case the next request corrects anyway. The read path is the
one place where the gap had a visible cost — re-reading a Space the caller has
just been removed from left nobody's permission to report — and it answers `404`
there, the same as for anyone else outside the Space.

Sort and lookup indexes take `spaceId` as their leading key because every query
filters on it first. The index initializer only creates indexes, so every
superseded name — the unscoped originals and the seven `owner_*` indexes that
followed them — is dropped explicitly before creation; otherwise an existing
deployment keeps unused indexes that still cost write time. The
retention `purgeAt` index stays Space-independent to match the purge path, which
remains the one reserved exception to "no bound Space, no query". Documents
written before the rename carry an `ownerId` no query reads, and nothing
converts them: disposable local data is recreated rather than backfilled.

## Provider single logout

Logout ends the provider session as well as the application session. This
reverses an earlier decision to keep logout application-only, and the reversal
is recorded rather than swapped in silently, because the reasons the first
decision gave were real and had to be answered rather than dismissed.

The first decision cited two costs. Provider logout needs an `id_token_hint`,
which means persisting the ID token in the authentication ticket purely to
support sign-out; and a `fetch` cannot follow a redirect into a provider page.
Both are still true. What changed is the weighting: the accepted trade-off was
that a sign-in immediately after sign-out completes with no credential prompt,
which on any shared device hands the next person the account. A sign-out
control that visibly returns to the login page while leaving the session
open at the provider is worse than no control, because it reports something it
did not do.

The two costs are paid as narrowly as the framework allows. Only the ID token
is stored, written into the ticket directly rather than by enabling
`SaveTokens`, which would have added the access and refresh tokens — credentials
the application never uses, since it calls no provider API. The ticket is
encrypted and its cookie is HttpOnly, so the "no tokens in the browser"
boundary is unchanged: nothing here is reachable from script, and the cookie
grows by about a kilobyte.

The redirect is handled by making logout a form post rather than a `fetch`, so
the browser owns the navigation. Keeping the `POST` keeps the global antiforgery
filter over it: validation reads the form field before the header when a request
has a form content type, so a browser-owned post can still carry the token. The
simpler option — a `GET` mirroring the login endpoint — was rejected because the
antiforgery filter does not cover `GET`, which would leave forced sign-out open
to any cross-site navigation. The cost of the form post is that failures surface
as pages rather than as errors, so the client checks `/api/auth/me` and takes a
fresh token immediately beforehand and falls back to a local clear when there is
no server session left to end.

Making logout a form post also pulled in the content security policy, which is
recorded because the interaction is not obvious and its failure mode is quiet.
`form-action` is checked against the redirects a submission follows, not only
against the address on the form, so a policy of `'self'` blocks the hop to the
provider. The application session would still end and the browser would still
land on the login page, so the user is told they signed out while the provider
session stands — the failure looks exactly like success. The policy is therefore
built from the configured authority rather than being a constant, and names that
origin in `form-action`.

What remains unbuilt is provider-initiated logout: Keycloak's back-channel and
front-channel logout, which end the application session when the sign-out starts
somewhere else in the single sign-on estate. That needs a registered endpoint and
server-side session storage keyed by the provider's session ID, because an
encrypted cookie cannot be revoked from outside the browser holding it. With one
application in the realm there is nowhere else for a sign-out to start.

## Antiforgery as a global requirement

Cookie authentication reintroduces cross-site request forgery, so state-changing
requests carry an antiforgery token in a request header, validated by a global
filter. Per-endpoint attributes were rejected for the same reason as per-handler
Space filters: an unprotected mutation should require a deliberate opt-out
rather than a remembered opt-in.

Antiforgery tokens are bound to the authenticated identity, so a token obtained
before login fails validation afterwards. The client requests a token whenever
a session is established — on startup and when the login navigation returns —
and discards it when the session ends. The antiforgery cookie may be readable
by JavaScript; it is not an authentication credential, and the session cookie
remains HttpOnly.

## Rate limiting keyed by user, not IP

Two limits exist, both partitioned by the internal user ID: a per-user
concurrency limit on assistant turns, and a per-user fixed-window limit on
mutation endpoints. The user's own model key removes the cost concern from
assistant turns, not the abuse one — a turn holds a model connection and a
request thread open for as long as the model takes — and a mutation loop
against the API is the same shape of problem with a cheaper body. Keying by
the user rather than the address means a refusal names the same caller the
request log does, and that everyone behind one NAT is not one caller.

The two are wired differently on purpose. The assistant limit is a named
policy attached to the one action it protects, so it is readable from the
endpoint. The mutation limit is the global limiter with a predicate: it
partitions to no limiter for `GET`, `HEAD`, and `OPTIONS`, for anonymous
requests, and for the turn route `/api/assistant/turns`. Reads are left alone
because they hold nothing past the response and paging a long list is the one
thing a well-behaved client does in a tight loop. Anonymous callers get no
limiter rather than a shared bucket, because authorization has already refused
them by the time the limiter runs — the middleware sits after it — and one
shared partition for everything unauthenticated would let any caller exhaust it
for the rest. The turn route is excluded so the limit a user actually hits is
one number rather than the interaction of two; the assistant's settings routes
carry no policy of their own — one of them opens an outbound connection to a
host the user names — so they stay inside the window like any other mutation.

Queue length is zero on both. A caller waiting for a permit is holding open the
connection the limit exists to protect, so refusing at once says the same thing
sooner. A refusal is a `429` in the same Problem Details shape as every other
error, carrying the `traceId`, with a `Retry-After` header only when the
limiter that refused can state a wait — a concurrency limit has no window to
wait out. The `RateLimiting` section holds the numbers and an `Enabled` switch;
the integration tests cover the window, the read exclusion, the per-user
partition, the assistant permit, the problem contract, and the switch.

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

## Token-prefix search over stored search tokens

Text search stores a normalized `searchTokens` array on each TODO and matches
each typed term as an index-backed prefix of some token, with several terms
combining as AND. Search is a plain filter joined into the existing list query,
so sorting and keyset paging are untouched.

MongoDB's own `$text` index was rejected: it matches whole words or stems, so
"quart" would not find "quarterly", and its relevance ordering fights the
keyset cursor the list already depends on. Embeddings were rejected because the
feature would then only work for users who have configured a provider, every
write would pay an API call, and `$vectorSearch` is not available on the
self-hosted `mongo:8.0` this deployment targets. An external search engine was
rejected as a second always-on service for one text box.

Search queries `hint()` the search index. Left to itself the planner can pick a
sort-supporting index and then apply the token regexes to the Space's entire
range, which is the slow path the feature exists to avoid. Non-search queries
are not hinted and keep whatever plan they have today. The hint costs the sort
its index, but no index can serve both a range on the tokens and the sort
order, so a top-K sort bounded by `limit + 1` is the best plan available rather
than a concession.

The accepted costs:

- **Residual filtering.** The index bounds serve the first term only. Remaining
  terms and the status, priority, and due-date filters are applied to fetched
  documents, each already Space-scoped, so the worst case is bounded by one
  Space's own TODO count.
- **Paging under search is O(match set) per page.** The cursor predicate has no
  indexed field after the tokens key, so each Load More re-fetches and top-K
  sorts every matching document. Keyset paging's O(page) property is given back
  while a search is active, again bounded by the Space's own TODOs.
- **Trash-scope search scans wide.** Selecting deleted TODOs is a range on
  `deletedAt`, which sits before the tokens key, so the token bounds apply per
  distinct value and the query effectively scans that Space's trash.
- **Write amplification.** A 2000-character description can produce roughly 250
  to 300 distinct tokens, all rewritten as multikey entries on every
  full-document replace. This is likely the collection's largest index — fine
  at this scale, recorded so `db.stats()` does not surprise anyone.
- **Rollback is safe but not self-healing.** `TodoDocument` ignores unknown
  elements, so a rolled-back binary reads token-carrying documents without
  complaint, but its full-document replaces drop `searchTokens` from anything
  edited while rolled back. Nothing backfills them: the startup migrator that
  once did was removed with the enum migrator, for the reason "Persisted enum
  representation" gives, so such a document stays invisible to search until a
  current build rewrites it. A non-event on the single-instance compose
  deployment; it matters only if deployment ever becomes rolling, which is
  when the migration step in §4 of the short log earns its place.
- **A match can be invisible.** Search covers description words, while a card
  renders only the first 120 characters of the description. A term matched deep
  in a long description produces a card with no visible occurrence of it. This
  is documented rather than fixed, so it is not reported as a bug.
- **Prefix, not substring.** The dependency picker used to filter its loaded
  candidates by substring, so "ilk" found "milk". It no longer does. That is
  the price of an index-backed match, and it buys the picker the whole
  collection instead of the first hundred candidates.

## Index creation at startup

A hosted service in the infrastructure module creates the MongoDB indexes when
the host starts. Registration sits inside `AddInfrastructure`, so every host
that composes the module gets them — the API and the integration tests alike —
without any caller remembering to ask. An exception out of `StartAsync` aborts
host startup, so the application never serves traffic having silently failed to
create an index. `IHostedService` was chosen over `BackgroundService` for that
reason: `ExecuteAsync` failures are governed by
`BackgroundServiceExceptionBehavior` and are far easier to swallow.

Several instances starting together is the case the implementation is written
around. Creating an index that already exists under the same name and key
specification is a no-op, so repeated starts are free. Superseded index names
are read once and dropped only if present, rather than issuing a drop per name
and paying a failed command for every index an earlier run already removed. A
collection that does not exist yet and an index another instance dropped first
are both tolerated by error code.

The accepted costs:

- **An index build blocks writes to its collection.** Harmless on the empty or
  small collections this runs against, and the reason a real deployment moves
  creation to the migration step §4 of the short log describes rather than
  paying it on every start.
- **The searching query depends on the initializer having run.** A `hint`
  naming an index that does not exist is rejected by the server, not downgraded
  to another plan, so a host that skipped creation fails every search instead
  of running it slowly. Safe while creation runs in the same process as the
  query; it is the coupling to carry forward if creation ever moves out.

## Deferred decisions

Retention cleanup scheduling remains deferred until its vertical slice.
Selecting the production identity provider is the remaining authentication
decision and is now a configuration choice rather than a design one.
