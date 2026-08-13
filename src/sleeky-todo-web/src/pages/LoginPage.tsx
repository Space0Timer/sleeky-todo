import { Navigate } from 'react-router'

import { buildLoginUrl } from '../api/auth.ts'
import { useAuth } from '../auth/AuthContext.ts'
import { Button, SessionStatus } from '../components/common/index.ts'
import styles from './LoginPage.module.scss'

export function LoginPage() {
  const { isAuthenticated, isLoading } = useAuth()

  if (isLoading) {
    return <SessionStatus />
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
    <main className={styles.loginPage}>
      <h1>Sleeky To-Do</h1>
      <p>Sign in to see the TODOs that belong to you.</p>
      <Button variant="primary" onClick={startLogin}>
        Sign in
      </Button>
    </main>
  )
}
