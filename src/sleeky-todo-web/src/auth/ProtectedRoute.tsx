import type { ReactNode } from 'react'
import { Navigate } from 'react-router'

import { useAuth } from './AuthContext.ts'

export function ProtectedRoute({ children }: { children: ReactNode }) {
  const { isAuthenticated, isLoading } = useAuth()

  if (isLoading) {
    return (
      <main className="session-status" aria-busy="true">
        <p>Checking your session…</p>
      </main>
    )
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />
  }

  return children
}
