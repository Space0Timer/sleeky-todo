import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'

import {
  getCurrentUser,
  logout,
  refreshAntiforgeryToken,
  type CurrentUser,
} from '../api/auth.ts'
import { setAntiforgeryToken } from '../api/http.ts'
import { AuthContext, type AuthState } from './AuthContext.ts'

const anonymous: CurrentUser = {
  displayName: null,
  isAuthenticated: false,
  userId: null,
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CurrentUser>(anonymous)
  const [isLoading, setIsLoading] = useState(true)

  /**
   * Antiforgery tokens are identity-bound, so one is requested whenever the
   * session is established and discarded whenever it ends.
   */
  const loadSession = useCallback(async () => {
    try {
      const current = await getCurrentUser()

      if (current.isAuthenticated) {
        await refreshAntiforgeryToken()
      } else {
        setAntiforgeryToken(null)
      }

      setUser(current)
    } catch {
      setAntiforgeryToken(null)
      setUser(anonymous)
    } finally {
      setIsLoading(false)
    }
  }, [])

  useEffect(() => {
    void loadSession()
  }, [loadSession])

  const endSession = useCallback(() => {
    setAntiforgeryToken(null)
    setUser(anonymous)
  }, [])

  const signOut = useCallback(async () => {
    try {
      if (await logout()) {
        // The browser is leaving for the provider's end-session endpoint.
        // Clearing state here would re-render the application against an
        // anonymous session for the moment before the document is replaced.
        return true
      }
    } catch {
      // A failure to reach the API leaves the server session in place, but
      // keeping the client signed in on top of it helps nobody: the local
      // state is dropped and the caller routes to the login page.
    }

    setAntiforgeryToken(null)
    setUser(anonymous)

    return false
  }, [])

  const value = useMemo<AuthState>(
    () => ({
      displayName: user.displayName,
      endSession,
      isAuthenticated: user.isAuthenticated,
      isLoading,
      signOut,
      userId: user.userId,
    }),
    [endSession, isLoading, signOut, user],
  )

  return <AuthContext value={value}>{children}</AuthContext>
}
