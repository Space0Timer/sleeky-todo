import { Navigate } from 'react-router'

import { EmptyState } from '../components/common/index.ts'
import { readLastSpaceId } from './lastSpace.ts'
import { resolveActiveSpace } from './resolveActiveSpace.ts'
import { useSpaces } from './SpaceContext.ts'

/**
 * `/` is never a page of its own: it picks a Space and moves there. The Space
 * the user last had open wins when they still have it, otherwise the oldest —
 * their personal one, which the server ensures on every list read.
 */
export function SpaceRedirect() {
  const { spaces } = useSpaces()
  const resolved = resolveActiveSpace(spaces, null, readLastSpaceId())

  // Unreachable while the server ensures a personal Space, but the resolver
  // says null is possible and a blank page is the wrong way to find out.
  if (resolved === null) {
    return (
      <main>
        <EmptyState>You do not have access to any space yet.</EmptyState>
      </main>
    )
  }

  return <Navigate to={`/spaces/${encodeURIComponent(resolved.id)}`} replace />
}
