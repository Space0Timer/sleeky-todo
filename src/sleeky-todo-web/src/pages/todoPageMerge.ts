import type { TodoListItem } from '../types/todo.ts'

/**
 * Appends a freshly fetched cursor page to the TODOs already on screen.
 *
 * Keyset pagination reads live data, so a TODO whose sort field is edited
 * between two page fetches can move behind the cursor and come back on a page
 * the user has already seen. Appending blindly would then show it twice, once
 * stale and once fresh, and the row key carries the version, so React renders
 * both without so much as a duplicate-key warning.
 *
 * Matching on id keeps one copy, and the highest version wins because version
 * only ever counts up. The survivor keeps the position of the copy already on
 * screen: a row the user has scrolled past is more disruptive moving than it
 * is sitting where they last saw it, and the next refresh puts it in its true
 * place anyway.
 *
 * The reverse drift, a TODO moving ahead of the cursor before the page that
 * would have carried it, has no client-side answer: the API never sends it, so
 * it is simply absent until the next refresh.
 */
export function mergeTodoPage(
  current: TodoListItem[],
  incoming: TodoListItem[],
): TodoListItem[] {
  const merged: TodoListItem[] = []
  const positions = new Map<string, number>()

  for (const item of [...current, ...incoming]) {
    const position = positions.get(item.id)

    if (position === undefined) {
      positions.set(item.id, merged.length)
      merged.push(item)
      continue
    }

    // An equal version is the same document, and replacing it would hand the
    // row a new object to re-render for no change the user could see.
    if (item.version > merged[position].version) merged[position] = item
  }

  return merged
}
