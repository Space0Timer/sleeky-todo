import { type SpaceSummary } from '../types/space.ts'

/**
 * Picks the Space a visit should land in, from what the user can actually
 * reach. The URL's own request wins, then the Space they were last in, then
 * the oldest one — the personal Space, which the server ensures on every list
 * call. Null only when the list is empty, which a signed-in user should never
 * see.
 *
 * Pure so it can be tested without a router: the caller reads the request from
 * the URL and the memory from storage, and only ever navigates to what this
 * returns.
 */
export function resolveActiveSpace(
  spaces: readonly SpaceSummary[],
  requestedId: string | null,
  rememberedId: string | null,
): SpaceSummary | null {
  return findAccessible(spaces, requestedId)
    ?? findAccessible(spaces, rememberedId)
    ?? spaces[0]
    ?? null
}

function findAccessible(
  spaces: readonly SpaceSummary[],
  id: string | null,
): SpaceSummary | null {
  if (id === null || id === '') return null

  return spaces.find((space) => space.id === id) ?? null
}
