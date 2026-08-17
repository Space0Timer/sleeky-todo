import { Navigate, useLocation } from 'react-router'

import { buildLoginUrl } from '../api/auth.ts'
import { useAuth } from '../auth/AuthContext.ts'
import { Button, SessionStatus } from '../components/common/index.ts'
import styles from './LoginPage.module.scss'

/**
 * Where to land after signing in: the local path a protected route handed
 * over, or the root. Only a same-origin path is accepted here, and the API
 * checks the same before it redirects, so a link cannot send a fresh sign-in
 * anywhere but into this application.
 */
function resolveReturnTo(state: unknown): string {
  if (typeof state !== 'object' || state === null || !('returnTo' in state)) return '/'
  const candidate: unknown = state.returnTo
  if (typeof candidate !== 'string' || !candidate.startsWith('/') || candidate.startsWith('//')) {
    return '/'
  }
  return candidate
}

export function LoginPage() {
  const { isAuthenticated, isLoading } = useAuth()
  const location = useLocation()
  const returnTo = resolveReturnTo(location.state)

  if (isLoading) {
    return <SessionStatus />
  }

  if (isAuthenticated) {
    return <Navigate to={returnTo} replace />
  }

  // A full navigation rather than fetch: the browser must follow the redirect
  // to the identity provider and back through the callback.
  const startLogin = () => {
    window.location.assign(buildLoginUrl(returnTo))
  }

  return (
    <main className={styles.loginPage}>
      <h1>Sleeky To-Do</h1>
      <p>Sign in to see the TODOs that belong to you.</p>
      <Button variant="primary" onClick={startLogin}>
        Sign in
      </Button>
    </main>
  )
}
