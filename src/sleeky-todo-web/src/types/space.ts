/**
 * A Space is the collaboration and authorization boundary a TODO list lives
 * in. Every TODO route is nested under one, and what the page may do there is
 * decided by the caller's permission in it — a lattice, so Owner includes
 * Write and Write includes Read.
 */
export const spacePermission = {
  read: 1,
  write: 2,
  owner: 3,
} as const

export type SpacePermission = (typeof spacePermission)[keyof typeof spacePermission]

export const spacePermissionLabels: Record<SpacePermission, string> = {
  [spacePermission.read]: 'Read',
  [spacePermission.write]: 'Write',
  [spacePermission.owner]: 'Owner',
}

export const subjectType = {
  user: 1,
} as const

export type SubjectType = (typeof subjectType)[keyof typeof subjectType]

/** The server caps a Space name at this length. */
export const maximumSpaceNameLength = 100

/** One row of the Space list: what the selector shows and what the page acts on. */
export type SpaceSummary = {
  id: string
  name: string
  permission: SpacePermission
}

/**
 * One member of a Space. `displayName` is null when the directory has no
 * record of the subject, which is what lets a card fall back to showing no
 * creator rather than an identifier.
 */
export type SpaceAccessEntry = {
  subjectId: string
  subjectType: SubjectType
  permission: SpacePermission
  displayName: string | null
}

export type Space = {
  id: string
  name: string
  access: SpaceAccessEntry[]
  /** The caller's own permission, not the Space's — the same field the summary carries. */
  permission: SpacePermission
  version: number
  createdAt: string
  updatedAt: string
}

/** Whether a member at this level may change anything in the Space. */
export function canWrite(permission: SpacePermission): boolean {
  return permission >= spacePermission.write
}
