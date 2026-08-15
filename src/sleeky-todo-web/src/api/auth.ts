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

/**
 * Ends the session through the provider, which means handing the browser a
 * redirect chain: the API answers with a redirect to Keycloak's end-session
 * endpoint, Keycloak returns to the sign-out callback, and the callback lands
 * on `/login`. A `fetch` cannot follow that — it would resolve on an opaque
 * response with the browser still on the application — so the request is
 * submitted as a real form and the browser owns the navigation.
 *
 * Resolves `true` once the form is submitted, meaning the document is being
 * replaced and the caller must not route anywhere itself. Resolves `false`
 * when there was no server session left to end, where the caller does its own
 * routing.
 */
export async function logout(): Promise<boolean> {
  const current = await getCurrentUser()

  if (!current.isAuthenticated) {
    setAntiforgeryToken(null)
    return false
  }

  // A navigation cannot report a failure the way a `fetch` can: a rejected
  // token answers with a bare 400 page rather than an error the client can
  // recover from. The token is therefore re-read immediately beforehand rather
  // than reusing one that may have been issued for an identity that has since
  // expired.
  const token = await send<AntiforgeryToken>('/api/auth/antiforgery')

  const form = document.createElement('form')
  form.method = 'post'
  form.action = '/api/auth/logout'
  form.hidden = true

  const field = document.createElement('input')
  field.type = 'hidden'
  field.name = token.formFieldName
  field.value = token.token
  form.append(field)

  document.body.append(form)
  form.submit()

  setAntiforgeryToken(null)

  return true
}
