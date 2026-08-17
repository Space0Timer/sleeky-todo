/**
 * The one thing kept in local storage: the Space the user last had open, so a
 * visit to `/` returns them to it. Nothing else about a Space is stored — the
 * list, the permission, and the name are read fresh from the server, and the
 * URL is the source of truth for which Space is open right now.
 */
const lastSpaceKey = 'sleeky-todo:last-space-id'

/**
 * Storage can be unavailable (a private window, a full quota, a locked-down
 * profile), and remembering the last Space is a convenience rather than a
 * requirement, so a failure to read or write it is swallowed rather than
 * surfaced.
 */
export function readLastSpaceId(): string | null {
  try {
    return window.localStorage.getItem(lastSpaceKey)
  } catch {
    return null
  }
}

export function rememberLastSpaceId(id: string): void {
  try {
    window.localStorage.setItem(lastSpaceKey, id)
  } catch {
    // Not remembering the Space is the whole extent of the failure.
  }
}
