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

## Application layout

- Give each command and query its own folder under `Todos/Commands` or
  `Todos/Queries`, named for the operation and holding that operation's request,
  handler, and validator.
- A folder under `Commands` or `Queries` is either one operation or a group of
  related operations. It is never a bag of helpers sitting beside the operations
  that use it.
- When operations share request, result, loading, or validation parts, put the
  shared parts in the folder that contains those operations, so the grouping is
  visible from the tree rather than from the file names. Bulk status change and
  bulk deletion are grouped this way under `Commands/Bulk`.
- Match namespaces to folder paths.
- Keep a type name unambiguous on its own. A bulk operation keeps its `Bulk`
  prefix even inside `Commands/Bulk`, because the single-item command of the
  same name exists alongside it.

## Boundaries and tests

- Keep infrastructure documents, serializers, and database details behind
  Application abstractions.
- Map persistence models to application DTOs at the boundary.
- Test through public methods, interfaces, handlers, repository contracts, or
  HTTP endpoints. Do not expose internals solely to make a test possible.
- Test observable behavior and persisted contracts, not private helper
  implementation details.

## Styling

Client styles are SCSS. Component styles are CSS Modules (`*.module.scss`), and
`src/index.scss` is the only global stylesheet. See the decision log for why
SCSS rather than the indented syntax, and why modules rather than global CSS.

- Take every colour, radius, border width, shadow, typeface, and weight from
  `src/styles/_tokens.scss`. A literal hex, `rgb()`, or `hsl()` anywhere else
  fails the lint.
- Apply typography with `@include m.typography(<style>)`. Declaring
  `font-size`, `font-weight`, `line-height`, or `letter-spacing` directly fails
  the lint; add a named style to `$-text-styles` in `styles/_mixins.scss`
  instead.
- Truncate text with `@include m.truncate`. The three declarations only work
  together, so `text-overflow: ellipsis` outside the mixin fails the lint.
- Share between modules through a mixin, never by importing another component's
  module: `surface`, `field`, `focus-ring`, `action-row`, `standalone-panel`,
  and `field-label` exist for this. Importing a sibling's stylesheet
  reintroduces the coupling that scoping removes.
- Name classes in kebab-case. Vite exposes them to components as camelCase, so
  `.todo-card` is read as `styles.todoCard`.
- Class names are type-checked. `yarn css-types` writes a declaration beside
  every module, and `yarn dev` and `yarn build` run it first, so a misspelled
  class fails the build instead of rendering an unstyled element. Those files
  are generated and git-ignored: regenerate them, do not edit them.
- Order a rule as `@include` first, then declarations, then nested rules.
- Write `border: 0` rather than `border: none`, keep hex lowercase, and do not
  use `!important`.
- `@use` only what the file references.

Declarations follow an idiomatic order: position, display and flex, box model,
spacing, borders, background, visual, typography, layout extras, transitions.
Within spacing, `padding` precedes `margin`.

Run `yarn lint:styles`, or `yarn lint:styles:fix` to reorder properties
automatically. Both run in CI.

Spacing is a known gap. The measurements this stylesheet inherited do not form a
scale, so they remain literals and no rule enforces them. Normalising them onto
a real scale means accepting sub-pixel visual changes and is a separate task.

## Verification and file hygiene

- Builds must pass with warnings treated as errors and StyleCop enabled.
- Run formatting verification before committing.
- Use `git diff --check` and do not leave blank lines at the end of files.
- Keep comments focused on non-obvious decisions; make routine code clear
  through structure and naming.
