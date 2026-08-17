import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useMatch } from 'react-router'

import { ApiError } from '../api/http.ts'
import { createSpace as postSpace, listSpaces } from '../api/spaces.ts'
import { useAuth } from '../auth/AuthContext.ts'
import { SessionStatus } from '../components/common/index.ts'
import { type Space, type SpaceSummary } from '../types/space.ts'
import { SpaceContext, type SpacesState } from './SpaceContext.ts'
import { SpaceLoadFailure } from './SpaceLoadFailure.tsx'

/** How long the "space no longer available" notice stays before clearing itself. */
const noticeDuration = 6000

const unavailableNotice = 'That space is no longer available.'

const spacePattern = '/spaces/:spaceId'

/**
 * Holds the Space list for every authenticated route and gates them on it:
 * nothing below can render until the first read has landed, because both the
 * redirect from `/` and the Space page itself decide what to show from it.
 *
 * Only that first read blocks. A refresh runs behind the list already on
 * screen, and a refresh that fails leaves it there — a stale list is more
 * useful than an error where the page was.
 */
export function SpaceProvider({ children }: { children: ReactNode }) {
  const [spaces, setSpaces] = useState<SpaceSummary[] | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const { endSession, isAuthenticated } = useAuth()

  const match = useMatch(spacePattern)
  const requestedId = match?.params.spaceId ?? null

  const activeSpace = useMemo(
    () => spaces?.find((space) => space.id === requestedId) ?? null,
    [requestedId, spaces],
  )

  const refreshSpaces = useCallback(async (): Promise<SpaceSummary[] | null> => {
    setLoading(true)
    try {
      const fresh = await listSpaces()
      setSpaces(fresh)
      setError(null)
      return fresh
    } catch (caught) {
      // A session the server no longer recognises is not a list-loading
      // failure: dropping the client's own session state is what sends the
      // protected routes back to the login page.
      if (caught instanceof ApiError && caught.kind === 'unauthorized') {
        endSession()
        return null
      }

      setError(caught instanceof Error ? caught.message : 'The Space list could not be read.')
      return null
    } finally {
      setLoading(false)
    }
  }, [endSession])

  useEffect(() => {
    if (!isAuthenticated) return
    void refreshSpaces()
  }, [isAuthenticated, refreshSpaces])

  /**
   * The created Space joins the list before anyone navigates to it, so the
   * route never sees a URL naming a Space the list does not yet hold — which
   * is exactly the state it treats as "no longer available". The refresh that
   * follows is for the rest of the row, not for the row to exist.
   */
  const createSpace = useCallback(async (name: string): Promise<Space> => {
    const created = await postSpace(name)

    setSpaces((current) => [
      ...(current ?? []),
      { id: created.id, name: created.name, permission: created.permission },
    ])
    void refreshSpaces()

    return created
  }, [refreshSpaces])

  const dismissNotice = useCallback(() => setNotice(null), [])

  const reportSpaceUnavailable = useCallback(() => setNotice(unavailableNotice), [])

  useEffect(() => {
    const timer = notice === null
      ? null
      : setTimeout(() => setNotice(null), noticeDuration)

    return () => {
      if (timer !== null) clearTimeout(timer)
    }
  }, [notice])

  const value = useMemo<SpacesState>(
    () => ({
      spaces: spaces ?? [],
      activeSpace,
      loading,
      error,
      refreshSpaces,
      createSpace,
      notice,
      dismissNotice,
      reportSpaceUnavailable,
    }),
    [
      activeSpace,
      createSpace,
      dismissNotice,
      error,
      loading,
      notice,
      refreshSpaces,
      reportSpaceUnavailable,
      spaces,
    ],
  )

  if (spaces === null) {
    return error === null
      ? <SessionStatus message="Loading your spaces…" />
      : (
        <SpaceLoadFailure
          detail={error}
          retrying={loading}
          onRetry={() => void refreshSpaces()}
        />
      )
  }

  return <SpaceContext value={value}>{children}</SpaceContext>
}
