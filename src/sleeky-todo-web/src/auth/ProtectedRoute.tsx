import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router'

import { SessionStatus } from '../components/common/index.ts'
import { useAuth } from './AuthContext.ts'

export function ProtectedRoute({ children }: { children: ReactNode }) {
  const { isAuthenticated, isLoading } = useAuth()
  const location = useLocation()

  if (isLoading) {
    return <SessionStatus />
  }

  if (!isAuthenticated) {
    // Carried through the login page as router state, so a shared link that
    // arrives signed out still opens the list it names once the sign-in
    // returns, rather than landing on whatever Space is remembered.
    return (
      <Navigate
        replace
        state={{ returnTo: location.pathname + location.search }}
        to="/login"
      />
    )
  }

  return children
}
