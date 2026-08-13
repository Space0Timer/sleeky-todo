import { Navigate } from 'react-router'

import { buildLoginUrl } from '../api/auth.ts'
import { useAuth } from '../auth/AuthContext.ts'

export function LoginPage() {
  const { isAuthenticated, isLoading } = useAuth()

  if (isLoading) {
    return (
      <main className="session-status" aria-busy="true">
        <p>Checking your session…</p>
      </main>
    )
  }

  if (isAuthenticated) {
    return <Navigate to="/" replace />
  }

  // A full navigation rather than fetch: the browser must follow the redirect
  // to the identity provider and back through the callback.
  const startLogin = () => {
    window.location.assign(buildLoginUrl('/'))
  }

  return (
    <main className="login-page">
      <h1>Sleeky To-Do</h1>
      <p>Sign in to see the TODOs that belong to you.</p>
      <button type="button" onClick={startLogin}>
        Sign in
      </button>
    </main>
  )
}
