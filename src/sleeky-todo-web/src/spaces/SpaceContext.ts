import { createContext, use } from 'react'

import { type Space, type SpaceSummary } from '../types/space.ts'

export type SpacesState = {
  /** Every Space the user is a member of, oldest first. Never empty once loaded. */
  spaces: SpaceSummary[]
  /**
   * The Space the URL names, or null when it names none the user can reach.
   * Derived from the route and the list rather than held: the URL is the only
   * record of which Space is open, so there is nothing to keep in step.
   */
  activeSpace: SpaceSummary | null
  /** A list fetch is in flight, whether the first one or a refresh. */
  loading: boolean
  /** Why the last fetch failed, cleared by the next one that succeeds. */
  error: string | null
  /**
   * Re-reads the list. Resolves with the fresh list, or null when the read
   * failed and the previous list is still what is shown.
   */
  refreshSpaces: () => Promise<SpaceSummary[] | null>
  /** Creates a Space with the user as its Owner and adds it to the list. */
  createSpace: (name: string) => Promise<Space>
  /**
   * A message about the list itself — today only that the Space a route named
   * has gone. It outlives the page that raised it, so it lives here and the
   * page that lands next renders it in its own toast region.
   */
  notice: string | null
  dismissNotice: () => void
  /** Records that the Space the URL names cannot be reached; the route then falls back to `/`. */
  reportSpaceUnavailable: () => void
}

export const SpaceContext = createContext<SpacesState | null>(null)

export function useSpaces(): SpacesState {
  const state = use(SpaceContext)

  if (state === null) {
    throw new Error('useSpaces must be used inside a SpaceProvider.')
  }

  return state
}

// A hot update cannot be applied to this module. Re-running it calls
// `createContext` again, and the new context is a different object from the one
// the mounted provider is still supplying — so a consumer that picked up the
// new module reads null and throws, while nothing is actually wrong. The update
// is taken and immediately handed back, which makes the dev server reload the
// page: the only way both halves end up on the same context. Invalidating
// outside the handler would instead fire on the very first evaluation, when
// there is no update to refuse, and reload a page that had only just loaded.
// It costs nothing in a build: `import.meta.hot` is statically undefined there
// and the branch is dropped.
if (import.meta.hot) {
  import.meta.hot.accept(() => {
    import.meta.hot?.invalidate()
  })
}
