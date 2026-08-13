import { useState } from 'react'
import { useNavigate } from 'react-router'

import { useAuth } from '../auth/AuthContext.ts'
import styles from './UserMenu.module.scss'
import { Button } from './common/index.ts'

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
      {/*
        The text variant rather than a filled one: this sits in a 15rem note
        panel beside the signed-in name, where a full control would outweigh
        the page's own actions.
      */}
      <Button
        variant="text"
        disabled={isSigningOut}
        onClick={() => void handleSignOut()}
      >
        {isSigningOut ? 'Signing out…' : 'Sign out'}
      </Button>
    </div>
  )
}
