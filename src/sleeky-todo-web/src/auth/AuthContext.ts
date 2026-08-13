import { createContext, use } from 'react'

export type AuthState = {
  displayName: string | null
  /**
   * Drops the client's view of the session without calling the API, for when
   * the server has already rejected the session as expired or missing.
   */
  endSession: () => void
  isAuthenticated: boolean
  isLoading: boolean
  signOut: () => Promise<void>
  userId: string | null
}

export const AuthContext = createContext<AuthState | null>(null)

export function useAuth(): AuthState {
  const state = use(AuthContext)

  if (state === null) {
    throw new Error('useAuth must be used inside an AuthProvider.')
  }

  return state
}
