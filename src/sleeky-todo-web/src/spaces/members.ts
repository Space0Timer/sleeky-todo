import { spacePermission, type SpaceAccessEntry, type SpacePermission } from '../types/space.ts'

/** One member as the settings dialog draws them. */
export type MemberRow = {
  subjectId: string
  /** What to call them: their display name, or a short form of the identifier. */
  label: string
  permission: SpacePermission
  /** Whether this row is the person reading it, which the dialog marks. */
  isCurrentUser: boolean
}

/**
 * How much of an identifier is shown when the directory has no name for a
 * subject. Enough to tell two members apart in a list, short enough to read.
 */
const identifierFallbackLength = 8

/**
 * The members of a Space, ordered for reading rather than as the server
 * happens to store them: the most privileged first, and alphabetically within
 * a level.
 *
 * Pure, and separate from the dialog, because the two decisions here are worth
 * stating on their own — a subject the directory holds no name for still needs
 * something to be called, and the reader needs to recognise their own row
 * before they change anyone's access.
 */
export function toMemberRows(
  access: readonly SpaceAccessEntry[],
  currentUserId: string | null,
): MemberRow[] {
  return access
    .map((entry) => ({
      subjectId: entry.subjectId,
      label: memberLabel(entry),
      permission: entry.permission,
      isCurrentUser: entry.subjectId === currentUserId,
    }))
    .sort(byPermissionThenLabel)
}

/**
 * A name when the directory has one. Otherwise the head of the identifier,
 * which says "someone whose name we do not have" rather than pretending the
 * row is empty.
 */
export function memberLabel(entry: SpaceAccessEntry): string {
  const name = entry.displayName?.trim() ?? ''

  return name === '' ? entry.subjectId.slice(0, identifierFallbackLength) : name
}

/** Whether a member at this level may change the Space itself, including who is in it. */
export function isOwner(permission: SpacePermission): boolean {
  return permission >= spacePermission.owner
}

function byPermissionThenLabel(left: MemberRow, right: MemberRow): number {
  return right.permission - left.permission
    || left.label.localeCompare(right.label)
}
