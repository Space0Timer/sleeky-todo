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

  const signOut = useCallback(async () => {
    try {
      await logout()
    } finally {
      setAntiforgeryToken(null)
      setUser(anonymous)
    }
  }, [])

  const value = useMemo<AuthState>(
    () => ({
      displayName: user.displayName,
      isAuthenticated: user.isAuthenticated,
      isLoading,
      signOut,
      userId: user.userId,
    }),
    [isLoading, signOut, user],
  )

  return <AuthContext value={value}>{children}</AuthContext>
}
