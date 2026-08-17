import { send } from './http.ts'
import {
  type Space,
  type SpaceAccessEntry,
  type SpacePermission,
  type SpaceSummary,
} from '../types/space.ts'

const spacesPath = '/api/spaces'

function spacePath(id: string): string {
  return `${spacesPath}/${encodeURIComponent(id)}`
}

function accessPath(id: string, subjectId: string): string {
  return `${spacePath(id)}/access/${encodeURIComponent(subjectId)}`
}

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
  return send<Space>(spacePath(id))
}

export function createSpace(name: string): Promise<Space> {
  return send<Space>(spacesPath, {
    method: 'POST',
    body: JSON.stringify({ name }),
  })
}

/** The Space's members with their display names, readable by any member. */
export function listSpaceAccess(id: string): Promise<SpaceAccessEntry[]> {
  return send<SpaceAccessEntry[]>(`${spacePath(id)}/access`)
}

/*
 * The four Owner-only mutations. Each carries the `version` the caller last
 * read and answers with the whole Space, so the caller replaces what it holds
 * rather than patching it — the version has moved on either way, and a
 * membership list assembled from a response and a stale copy is exactly the
 * thing the version exists to prevent.
 *
 * A version the server has moved past is a 409 (`kind === 'concurrency'`).
 * None of these retries on one: replaying a security change against a Space
 * that changed underneath would apply an intent formed against a list the
 * caller can no longer see.
 */

export function renameSpace(id: string, name: string, version: number): Promise<Space> {
  return send<Space>(spacePath(id), {
    method: 'PUT',
    body: JSON.stringify({ name, version }),
  })
}

export function addSpaceAccess(
  id: string,
  subjectId: string,
  permission: SpacePermission,
  version: number,
): Promise<Space> {
  return send<Space>(`${spacePath(id)}/access`, {
    method: 'POST',
    body: JSON.stringify({ subjectId, permission, version }),
  })
}

export function changeSpacePermission(
  id: string,
  subjectId: string,
  permission: SpacePermission,
  version: number,
): Promise<Space> {
  return send<Space>(accessPath(id, subjectId), {
    method: 'PUT',
    body: JSON.stringify({ permission, version }),
  })
}

/** The version travels in the body, the same shape a TODO deletion uses. */
export function removeSpaceAccess(
  id: string,
  subjectId: string,
  version: number,
): Promise<Space> {
  return send<Space>(accessPath(id, subjectId), {
    method: 'DELETE',
    body: JSON.stringify({ version }),
  })
}
