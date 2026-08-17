/**
 * Someone a search found, offered as a candidate to share a Space with.
 *
 * Both descriptive fields are optional because the identity provider decides
 * what it publishes: a directory entry always has an identifier and may have
 * neither a name nor an address. Only users who have signed in at least once
 * are in the directory, so someone who has never opened the application cannot
 * be found here — and cannot be granted access either.
 */
export type UserSummary = {
  id: string
  displayName: string | null
  email: string | null
}

/** The server refuses a shorter term rather than answering with a slice of everybody. */
export const minimumUserSearchLength = 2
