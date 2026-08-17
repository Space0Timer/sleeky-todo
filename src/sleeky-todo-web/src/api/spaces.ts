import { send } from './http.ts'
import { type Space, type SpaceAccessEntry, type SpaceSummary } from '../types/space.ts'

const spacesPath = '/api/spaces'

/**
 * Every Space the signed-in user is a member of, oldest first. The server
 * ensures the user's personal Space on this call, so the list is never empty
 * for a signed-in user.
 */
export function listSpaces(): Promise<SpaceSummary[]> {
  return send<SpaceSummary[]>(spacesPath)
}

/**
 * Answers 404 for a Space the user is not a member of as well as for one that
 * does not exist: the server does not confirm what it will not show.
 */
export function getSpace(id: string): Promise<Space> {
  return send<Space>(`${spacesPath}/${encodeURIComponent(id)}`)
}

export function createSpace(name: string): Promise<Space> {
  return send<Space>(spacesPath, {
    method: 'POST',
    body: JSON.stringify({ name }),
  })
}

/** The Space's members with their display names, readable by any member. */
export function listSpaceAccess(id: string): Promise<SpaceAccessEntry[]> {
  return send<SpaceAccessEntry[]>(`${spacesPath}/${encodeURIComponent(id)}/access`)
}
