import { send } from './http.ts'
import { type UserSummary } from '../types/user.ts'

/**
 * Users whose display name or e-mail address starts with `query`, at most ten
 * of them, never including the caller. A term shorter than two characters is
 * refused by the server rather than answered, so callers debounce and let the
 * validation speak for itself.
 */
export function searchUsers(query: string): Promise<UserSummary[]> {
  return send<UserSummary[]>(`/api/users/search?q=${encodeURIComponent(query)}`)
}
