import { useState } from 'react'
import { useNavigate } from 'react-router'

import { useAuth } from '../auth/AuthContext.ts'
import styles from './UserMenu.module.scss'

export function UserMenu() {
  const { displayName, signOut } = useAuth()
  const navigate = useNavigate()
  const [isSigningOut, setIsSigningOut] = useState(false)

  const handleSignOut = async () => {
    setIsSigningOut(true)

    try {
      await signOut()
      await navigate('/login', { replace: true })
    } finally {
      setIsSigningOut(false)
    }
  }

  return (
    <div className={styles.userMenu}>
      <span data-testid="current-user">{displayName ?? 'Signed in'}</span>
      <button
        type="button"
        onClick={() => void handleSignOut()}
        disabled={isSigningOut}
      >
        {isSigningOut ? 'Signing out…' : 'Sign out'}
      </button>
    </div>
  )
}
