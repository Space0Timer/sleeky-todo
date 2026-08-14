#!/usr/bin/env bash
#
# Fails the build on patterns that docs/coding-standards.md forbids but no
# analyzer detects. Keep each entry paired with the rule it enforces, and delete
# an entry as soon as an analyzer can make the same call.

set -euo pipefail

status=0

fail() {
  echo "::error file=$1::$2"
  status=1
}

# "Do not expose internals solely to make a test possible." Tests reach
# production code through interfaces, handlers, repository contracts, or HTTP
# endpoints; an internal implementation is resolved from the container instead.
while IFS= read -r match; do
  file="${match%%:*}"
  fail "$file" "InternalsVisibleTo is banned. Resolve the contract from the container instead of exposing the implementation."
done < <(grep -rn "InternalsVisibleTo" --include="*.csproj" --include="*.props" --include="*.cs" src tests || true)

if [ "$status" -eq 0 ]; then
  echo "No banned patterns found."
fi

exit "$status"
