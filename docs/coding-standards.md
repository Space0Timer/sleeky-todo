# Code Standards

This document defines the implementation and test standards for the repository.

## Readable implementation

- Keep one top-level type per C# file.
- Break behavior into small methods with one clear responsibility.
- Prefer guard clauses and early returns.
- Avoid `else` branches and nested conditionals when a guard clause or helper
  method expresses the same rule more clearly.
- Use descriptive names and keep domain rules close to the code that enforces
  them.
- Replace repeated literals, persisted field names, event IDs, and protocol
  values with named constants or enums. A literal is acceptable when it is a
  local structural value whose meaning is obvious.

## Error handling

- Catch the most specific exception type that can be handled.
- Do not catch `Exception` in ordinary application or infrastructure methods.
- A broad catch is permitted only at the outer process boundary where the host
  must log an unexpected failure and terminate safely.
- Preserve the original exception as the inner exception when translating an
  error into an application-specific exception.
- Validate external input at the boundary and fail with a stable, predictable
  error contract.

## Resource usage

- Project only fields required by the caller.
- Bound list results before materializing them in memory.
- Avoid loading full related documents when an identifier or count is enough.
- Prefer indexed filters and deterministic sort keys.
- Do not add temporary persisted or aggregation fields when the stored value
  already supports the required operation.
- Keep generated files, build output, and local environment artifacts out of
  source control.

## Boundaries and tests

- Keep infrastructure documents, serializers, and database details behind
  Application abstractions.
- Map persistence models to application DTOs at the boundary.
- Test through public methods, interfaces, handlers, repository contracts, or
  HTTP endpoints. Do not expose internals solely to make a test possible.
- Test observable behavior and persisted contracts, not private helper
  implementation details.

## Verification and file hygiene

- Builds must pass with warnings treated as errors and StyleCop enabled.
- Run formatting verification before committing.
- Use `git diff --check` and do not leave blank lines at the end of files.
- Keep comments focused on non-obvious decisions; make routine code clear
  through structure and naming.
