import { createContext, use } from 'react'

export type AuthState = {
  displayName: string | null
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
