# MongoDB TODO List Query

This document records the persisted enum representation, migration behavior,
aggregation pipeline, indexing assumptions, and implementation constraints for
the TODO list reader.

## Enum storage

`TodoStatus` and `TodoPriority` are stored as BSON `int32` values. Their explicit
numeric values are the business sort order:

```text
TodoPriority: Low=0, Medium=1, High=2
TodoStatus: NotStarted=0, InProgress=1, Completed=2, Archived=3
```

Enum numeric values are persistence contracts. Existing values must never be
renumbered. New values should be appended unless a separate data migration is
designed and deployed.

Integer storage allows MongoDB to filter, cursor-page, and sort the persisted
fields directly. The reader does not create temporary rank fields or evaluate
`$switch` expressions. The existing compound indexes on priority and status can
therefore support the requested ordering.

## String-to-integer migration

`MongoDbEnumStorageMigrator` runs during application startup before index
initialization. It performs an in-place migration step:

1. Find distinct stored values in `status` and `priority`.
2. Stop startup if a value is not a recognized enum name or numeric value,
   without modifying either field.
3. Replace each known string with its corresponding integer using idempotent
   `UpdateMany` operations.
4. Log the number of modified status and priority fields.

Restarting after success or partial execution is safe because integer values do
not match the string update filters. New application writes use integers only.

This is not a mixed-version rolling migration. Stop old application instances
before starting the new version so an old writer cannot reintroduce string
values after migration. Start the new version only after taking a recoverable
database backup or snapshot.

Rolling back to a binary that expects string enums requires a reverse migration
before that binary starts. Map `status` values `0..3` and `priority` values
`0..2` back to their enum names. The application does not perform this
destructive contraction automatically.

## Fluent aggregation design

The reader uses typed MongoDB filters, cursor comparisons, sorting, and limits.
Raw BSON stages remain only where the MongoDB aggregation expression is clearer
than a driver expression: the self-lookup, dependency count, and final computed
projection.

The base pipeline is:

```text
scope and field match
  -> cursor match on the persisted sort field and _id
```

The cursor comparison is equivalent to:

```text
sortField > lastSortValue
OR (sortField == lastSortValue AND _id > lastTodoId)
```

Both comparisons reverse for descending order. Sorting uses the same persisted
field followed by `_id`, so equal values remain deterministic and indexable.

### Requests without a dependency-state filter

```text
base match
  -> sort
  -> limit
  -> lookup completed dependencies
  -> calculate incompleteDependencyCount
  -> project the list row
```

Sorting and limiting before `$lookup` bounds dependency work to at most
`requested limit + 1` TODOs.

### Blocked or unblocked requests

```text
base match
  -> lookup completed dependencies
  -> calculate incompleteDependencyCount
  -> match blocked/unblocked
  -> sort
  -> limit
  -> project the list row
```

Dependency filtering must happen before the limit so pages contain the requested
state rather than filtering an already-truncated page.

## Dependency calculation

The lookup joins only dependencies that are present, not deleted, and completed.
It projects only `_id`; full dependency documents are never copied through the
pipeline. The blocked calculation is:

```text
incompleteDependencyCount = dependencyIds.Count - completedDependencyIds.Count
isBlocked = incompleteDependencyCount > 0
```

This preserves the domain rule that missing, deleted, archived, or unfinished
dependencies are incomplete. It also reduces aggregation memory and network
usage compared with loading every dependency document.

## Projection and boundaries

The final `$project` returns only fields required by `TodoListItemDto` and maps
them through the internal `MongoTodoListRow`. Infrastructure BSON documents do
not cross into Application. Tests exercise `ITodoListReader`, `ITodoRepository`,
or the HTTP API; internal persistence classes are not exposed for tests.

The implementation follows these constraints:

- one top-level type per C# file;
- StyleCop-clean builds with no warnings;
- early returns instead of `else` blocks or nested conditionals;
- specific exception handling except at the process boundary;
- constants for persisted MongoDB field names;
- small methods that each build one filter, stage, sort, or mapping operation;
- bounded result materialization and projections that avoid unused BSON data.
