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

    const isRedirecting = await signOut().catch(() => false)

    if (isRedirecting) {
      // Sign-out is going through the provider, so the browser is already
      // navigating. Routing here would race a navigation it owns, and the
      // pending label stays up until the document is replaced rather than
      // flicking back for the moment before it goes.
      return
    }

    await navigate('/login', { replace: true })
    setIsSigningOut(false)
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
