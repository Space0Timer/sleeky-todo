import { useEffect } from 'react'
import { useNavigate } from 'react-router'

import { SessionStatus } from '../components/common/index.ts'
import { TodosPage } from '../pages/TodosPage.tsx'
import { rememberLastSpaceId } from './lastSpace.ts'
import { useSpaces } from './SpaceContext.ts'

/**
 * `/spaces/:spaceId`. The list is already loaded by the time this renders, so
 * the only question is whether the URL names a Space the user can reach.
 *
 * When it does, the page is keyed on the Space: switching remounts it whole,
 * and every piece of Space-specific state — filters, cursor, items, selection,
 * open editors, the assistant's transcript and any pending confirmation — is
 * local state that goes with it. Nothing is cleared by hand.
 *
 * When it does not — an unknown identifier, or a membership revoked since the
 * list was last read — the route falls back to `/` and leaves a notice for the
 * page that lands next. The provider makes the same call after a Space-scoped
 * request answers 404, which is how a revocation mid-session arrives here.
 */
export function SpaceRoute() {
  const { activeSpace, reportSpaceUnavailable } = useSpaces()
  const navigate = useNavigate()

  useEffect(() => {
    if (activeSpace !== null) {
      rememberLastSpaceId(activeSpace.id)
      return
    }

    reportSpaceUnavailable()
    void navigate('/', { replace: true })
  }, [activeSpace, navigate, reportSpaceUnavailable])

  if (activeSpace === null) {
    return <SessionStatus message="Finding your space…" />
  }

  return <TodosPage key={activeSpace.id} space={activeSpace} />
}
