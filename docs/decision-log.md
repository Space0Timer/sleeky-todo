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

MongoDB mutations atomically filter by TODO ID and expected version, write version `N + 1`, and return the persisted document. A missing match indicates a stale write, which the application represents as `ConcurrencyConflictException`. The API maps this known exception to HTTP 409 Problem Details.

This design avoids locks and prevents lost updates. Concurrent integration tests verify that exactly one mutation succeeds when two writers submit update, delete, or restore operations using the same version.

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
