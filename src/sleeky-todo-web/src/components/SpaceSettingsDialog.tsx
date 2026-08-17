import { useCallback, useEffect, useState, type FormEvent } from 'react'

import { ApiError } from '../api/http.ts'
import {
  addSpaceAccess,
  changeSpacePermission,
  getSpace,
  removeSpaceAccess,
  renameSpace,
} from '../api/spaces.ts'
import { searchUsers } from '../api/users.ts'
import { useAuth } from '../auth/AuthContext.ts'
import { useDebouncedValue } from '../hooks/useDebouncedValue.ts'
import { isOwner, toMemberRows, type MemberRow } from '../spaces/members.ts'
import { useSpaces } from '../spaces/SpaceContext.ts'
import {
  maximumSpaceNameLength,
  spacePermission,
  spacePermissionLabels,
  type Space,
  type SpacePermission,
  type SpaceSummary,
} from '../types/space.ts'
import { minimumUserSearchLength, type UserSummary } from '../types/user.ts'
import styles from './SpaceSettingsDialog.module.scss'
import { Badge, Button, FieldError, Toast, ToastRegion } from './common/index.ts'

type SpaceSettingsDialogProps = {
  space: SpaceSummary
  onClose: () => void
}

const conflictNotice =
  'This space changed while the dialog was open. What you see is the latest.'

/** The levels a member can be given. Owner is offered as a promotion, not as a share default. */
const grantablePermissions: SpacePermission[] = [
  spacePermission.read,
  spacePermission.write,
  spacePermission.owner,
]

/**
 * Who is in a Space and what they may do there, plus its name.
 *
 * Read by any member — seeing who else shares a list is part of knowing what
 * you are working in — and changed only by an Owner, which is the same split
 * the server enforces. The controls a non-Owner would only be refused are not
 * drawn at all.
 *
 * Every mutation carries the `version` last read and replaces the whole Space
 * with the answer. A version the server has moved past comes back as a
 * conflict, and this is one of the places where retrying it would be wrong: an
 * access change is formed against a membership list, and if that list has
 * moved the intent no longer means what it did. The dialog re-reads, says so,
 * and leaves the next move to the person making it.
 */
export function SpaceSettingsDialog({ space, onClose }: SpaceSettingsDialogProps) {
  const { refreshSpaces } = useSpaces()
  const { userId } = useAuth()

  const [detail, setDetail] = useState<Space | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [failure, setFailure] = useState<string | null>(null)
  const [conflict, setConflict] = useState<string | null>(null)

  const [name, setName] = useState('')
  const [nameErrors, setNameErrors] = useState<string[] | undefined>(undefined)

  const [searchInput, setSearchInput] = useState('')
  const searchTerm = useDebouncedValue(searchInput).trim()
  const [results, setResults] = useState<UserSummary[]>([])
  const [searching, setSearching] = useState(false)
  const [searchError, setSearchError] = useState<string | null>(null)
  const [candidate, setCandidate] = useState<UserSummary | null>(null)
  const [grant, setGrant] = useState<SpacePermission>(spacePermission.write)

  /**
   * Adopts what the server holds, including into the rename field. It is
   * called on open and again after a conflict, and both times the point is
   * that the box shows the name the version belongs to.
   */
  const adopt = useCallback((fresh: Space) => {
    setDetail(fresh)
    setName(fresh.name)
    setLoadError(null)
  }, [])

  const reload = useCallback(async (): Promise<void> => {
    try {
      adopt(await getSpace(space.id))
    } catch (caught) {
      setLoadError(caught instanceof Error ? caught.message : 'The space could not be read.')
    }
  }, [adopt, space.id])

  /**
   * The opening read, guarded so a response that is no longer wanted cannot
   * land. Without it a second read still in flight — a remount, or the double
   * effect invocation development runs — would arrive after the user has begun
   * typing and put the stored name back in the box under them.
   */
  useEffect(() => {
    let current = true

    getSpace(space.id)
      .then((fresh) => {
        if (current) adopt(fresh)
      })
      .catch((caught: unknown) => {
        if (!current) return
        setLoadError(
          caught instanceof Error ? caught.message : 'The space could not be read.',
        )
      })

    return () => {
      current = false
    }
  }, [adopt, space.id])

  const members: MemberRow[] = toMemberRows(detail?.access ?? [], userId)
  const canManage = detail !== null && isOwner(detail.permission)
  const alreadyMember = (id: string) => members.some((member) => member.subjectId === id)

  /**
   * The search only ever asks for a term the server would accept, so the
   * shorter one a user is still typing clears the list instead of collecting a
   * validation error they have not finished causing.
   */
  useEffect(() => {
    if (!canManage || searchTerm.length < minimumUserSearchLength) {
      setResults([])
      setSearchError(null)
      return
    }

    let current = true
    setSearching(true)

    searchUsers(searchTerm)
      .then((found) => {
        if (!current) return
        setResults(found)
        setSearchError(null)
      })
      .catch((caught: unknown) => {
        if (!current) return
        setResults([])
        setSearchError(caught instanceof Error ? caught.message : 'The search failed.')
      })
      .finally(() => {
        if (current) setSearching(false)
      })

    // A slower earlier request must not overwrite a later one's results, and a
    // response arriving after the dialog closes must not touch state at all.
    return () => {
      current = false
    }
  }, [canManage, searchTerm])

  /**
   * Runs one Owner-only change against the version the dialog last read.
   * Everything that is the same for all four lives here: the version, the
   * whole-Space replacement, the Space list refresh so a rename reaches the
   * selector, and the conflict rule.
   */
  async function apply(
    key: string,
    mutate: (version: number) => Promise<Space>,
  ): Promise<boolean> {
    if (detail === null) return false

    setBusy(key)
    setFailure(null)
    setConflict(null)
    setNameErrors(undefined)

    try {
      adopt(await mutate(detail.version))
      void refreshSpaces()
      return true
    } catch (caught) {
      await report(caught)
      return false
    } finally {
      setBusy(null)
    }
  }

  async function report(caught: unknown): Promise<void> {
    if (caught instanceof ApiError && caught.kind === 'concurrency') {
      await reload()
      setConflict(conflictNotice)
      void refreshSpaces()
      return
    }

    if (caught instanceof ApiError && caught.kind === 'validation') {
      setNameErrors(caught.problem.errors?.name ?? [caught.message])
      return
    }

    // A Space that has gone from under the reader is not a failure of the
    // action they took: refreshing the list is what sends the route somewhere
    // they can still reach.
    if (caught instanceof ApiError && caught.kind === 'not-found') {
      void refreshSpaces()
    }

    setFailure(caught instanceof Error ? caught.message : 'The change could not be saved.')
  }

  function handleRename(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const trimmed = name.trim()
    if (trimmed === '') {
      setNameErrors(['A space name is required.'])
      return
    }

    void apply('rename', (version) => renameSpace(space.id, trimmed, version))
  }

  function handleAdd(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (candidate === null) return

    const subjectId = candidate.id

    // The chosen person and the term that found them are cleared only once the
    // grant has landed, so a refusal leaves the search where it was rather than
    // making the user find them again.
    void apply('add', (version) => addSpaceAccess(space.id, subjectId, grant, version))
      .then((added) => {
        if (!added) return
        setCandidate(null)
        setSearchInput('')

        // Cleared here rather than left to the debounced effect, which would
        // otherwise leave the person who was just added sitting under an empty
        // search box for as long as the pause lasts.
        setResults([])
      })
  }

  const dialogBody = detail === null
    ? <p className={styles.status}>{loadError ?? 'Loading the space…'}</p>
    : (
      <>
        {canManage
          ? (
            <form className={styles.rename} onSubmit={handleRename} noValidate>
              <fieldset disabled={busy !== null}>
                <label>
                  Name
                  <input
                    data-testid="space-rename-input"
                    maxLength={maximumSpaceNameLength}
                    name="name"
                    value={name}
                    onChange={(event) => setName(event.target.value)}
                  />
                </label>
                <FieldError messages={nameErrors} />
                <Button
                  data-testid="space-rename-submit"
                  type="submit"
                  variant="secondary"
                >
                  {busy === 'rename' ? 'Saving…' : 'Rename'}
                </Button>
              </fieldset>
            </form>
          )
          : <p className={styles.status}>{detail.name}</p>}

        <section className={styles.section}>
          <h3>Members</h3>
          <ul className={styles.members} data-testid="member-list">
            {members.map((member) => (
              <li
                className={styles.member}
                data-testid={`member-row-${member.subjectId}`}
                key={member.subjectId}
              >
                <span className={styles.memberName}>
                  {member.label}
                  {member.isCurrentUser && <Badge tone="neutral">you</Badge>}
                </span>

                {/*
                  Your own row is read-only even for an Owner. The server
                  refuses a self-removal outright and refuses to demote the last
                  Owner, so the controls would mostly be there to be rejected;
                  an Owner who wants out is removed by another Owner.
                */}
                {canManage && !member.isCurrentUser
                  ? (
                    <span className={styles.memberActions}>
                      <select
                        data-testid={`member-permission-${member.subjectId}`}
                        disabled={busy !== null}
                        value={member.permission}
                        onChange={(event) => {
                          const next = Number(event.target.value) as SpacePermission
                          void apply(
                            `permission-${member.subjectId}`,
                            (version) => changeSpacePermission(
                              space.id,
                              member.subjectId,
                              next,
                              version,
                            ),
                          )
                        }}
                      >
                        {grantablePermissions.map((level) => (
                          <option key={level} value={level}>
                            {spacePermissionLabels[level]}
                          </option>
                        ))}
                      </select>
                      <Button
                        data-testid={`member-remove-${member.subjectId}`}
                        disabled={busy !== null}
                        variant="danger"
                        onClick={() => void apply(
                          `remove-${member.subjectId}`,
                          (version) => removeSpaceAccess(space.id, member.subjectId, version),
                        )}
                      >
                        Remove
                      </Button>
                    </span>
                  )
                  : <Badge tone="info">{spacePermissionLabels[member.permission]}</Badge>}
              </li>
            ))}
          </ul>
        </section>

        {canManage && (
          <section className={styles.section}>
            <h3>Add a member</h3>
            <form className={styles.add} onSubmit={handleAdd} noValidate>
              <fieldset disabled={busy !== null}>
                <label>
                  Search by name or e-mail
                  <input
                    autoComplete="off"
                    data-testid="member-search"
                    name="member-search"
                    value={searchInput}
                    onChange={(event) => {
                      setSearchInput(event.target.value)
                      setCandidate(null)
                    }}
                  />
                </label>

                {searchError !== null && (
                  <p className={styles.status} role="alert">{searchError}</p>
                )}

                {candidate === null && (
                  <ul className={styles.results} data-testid="member-results">
                    {results.map((user) => (
                      <li key={user.id}>
                        <button
                          className={styles.result}
                          data-testid={`member-result-${user.id}`}
                          disabled={alreadyMember(user.id)}
                          type="button"
                          onClick={() => setCandidate(user)}
                        >
                          <span>{user.displayName ?? user.email ?? user.id}</span>
                          {user.email !== null && <small>{user.email}</small>}
                          {alreadyMember(user.id) && <small>Already a member</small>}
                        </button>
                      </li>
                    ))}
                    {searchTerm.length >= minimumUserSearchLength
                      && !searching
                      && results.length === 0
                      && searchError === null && (
                      <li className={styles.status}>
                        Nobody matches. Only people who have signed in at least once
                        can be found.
                      </li>
                    )}
                  </ul>
                )}

                {candidate !== null && (
                  <div className={styles.candidate} data-testid="member-candidate">
                    <span>{candidate.displayName ?? candidate.email ?? candidate.id}</span>
                    <select
                      aria-label="Permission"
                      data-testid="add-member-permission"
                      value={grant}
                      onChange={(event) =>
                        setGrant(Number(event.target.value) as SpacePermission)}
                    >
                      <option value={spacePermission.read}>
                        {spacePermissionLabels[spacePermission.read]}
                      </option>
                      <option value={spacePermission.write}>
                        {spacePermissionLabels[spacePermission.write]}
                      </option>
                    </select>
                    <Button
                      data-testid="add-member-submit"
                      type="submit"
                      variant="primary"
                    >
                      {busy === 'add' ? 'Adding…' : 'Add'}
                    </Button>
                  </div>
                )}
              </fieldset>
            </form>
          </section>
        )}

        {failure !== null && (
          <div className={styles.failure} role="alert">{failure}</div>
        )}
      </>
    )

  return (
    <>
      <div className={styles.backdrop} role="presentation">
        <dialog
          className={styles.dialog}
          aria-label="Space settings"
          data-testid="space-settings-dialog"
          open
          onKeyDown={(event) => {
            if (event.key === 'Escape' && busy === null) onClose()
          }}
        >
          <h2>Space settings</h2>

          {dialogBody}

          <div className={styles.dialogActions}>
            <Button data-testid="space-settings-close" variant="secondary" onClick={onClose}>
              Close
            </Button>
          </div>
        </dialog>
      </div>

      {/*
        Outside the backdrop rather than inside it: the toast region is drawn
        above the modal layer, and a conflict is exactly the message that must
        not be hidden by the dialog that raised it.
      */}
      {conflict !== null && (
        <ToastRegion>
          <Toast
            title={conflict}
            tone="warning"
            onDismiss={() => setConflict(null)}
          />
        </ToastRegion>
      )}
    </>
  )
}
