import { send, setAntiforgeryToken, type AntiforgeryToken } from './http.ts'

export type CurrentUser = {
  displayName: string | null
  isAuthenticated: boolean
  userId: string | null
}

export function getCurrentUser(): Promise<CurrentUser> {
  return send<CurrentUser>('/api/auth/me')
}

export async function refreshAntiforgeryToken(): Promise<void> {
  const token = await send<AntiforgeryToken>('/api/auth/antiforgery')
  setAntiforgeryToken(token)
}

export function buildLoginUrl(returnUrl: string): string {
  return `/api/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`
}

export async function logout(): Promise<void> {
  await send<void>('/api/auth/logout', { method: 'POST' })
  setAntiforgeryToken(null)
}
