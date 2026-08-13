import type { ReactNode } from 'react'
import { Navigate } from 'react-router'

import { SessionStatus } from '../components/common/index.ts'
import { useAuth } from './AuthContext.ts'

export function ProtectedRoute({ children }: { children: ReactNode }) {
  const { isAuthenticated, isLoading } = useAuth()

  if (isLoading) {
    return <SessionStatus />
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />
  }

  return children
}
